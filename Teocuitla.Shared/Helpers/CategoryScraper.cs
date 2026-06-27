using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Teocuitla.Shared.Models;
using Teocuitla.Shared.Helpers;

namespace Teocuitla.Shared.Helpers
{
    public class CategoryProductResult
    {
        public string Nombre { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public string UrlProducto { get; set; } = string.Empty;
        public string? ImagenUrl { get; set; }
        public string? Sku { get; set; }
        public bool YaExiste { get; set; }
    }

    public class CategoryScrapeResult
    {
        public bool Exitoso { get; set; }
        public List<CategoryProductResult> Productos { get; set; } = new();
        public int PaginasProcesadas { get; set; }
        public string? ErrorMensaje { get; set; }
        public string? RecommendedStrategy { get; set; }
        
        // Selectores sugeridos por la heurística (para aprendizaje)
        public string? LearnedSelectorProducto { get; set; }
        public string? LearnedSelectorNombre { get; set; }
        public string? LearnedSelectorPrecio { get; set; }
        public string? LearnedSelectorLink { get; set; }
        public string? LearnedSelectorImagen { get; set; }
        public string? LearnedSelectorPaginador { get; set; }

        // Diagnósticos detallados de la corrida
        public List<string> Diagnosticos { get; set; } = new();
    }

    public class CategoryScraper
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public CategoryScraper(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<CategoryScrapeResult> ScrapeCategoryAsync(
            string urlInicial, 
            CatalogoSitio sitio, 
            RegistroProxy? proxy = null,
            int maxPaginas = 3,
            string? overrideContainer = null,
            string? overrideNombre = null,
            string? overridePrecio = null,
            string? overrideLink = null,
            string? overrideImagen = null,
            string? overridePaginador = null)
        {
            var resultadoGlobal = new CategoryScrapeResult();
            var urlActual = urlInicial;
            int pagina = 0;
            var urlsProcesadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrWhiteSpace(urlActual) && pagina < maxPaginas)
            {
                if (urlsProcesadas.Contains(urlActual))
                {
                    // Evitar bucles infinitos de paginación
                    break;
                }
                urlsProcesadas.Add(urlActual);
                pagina++;

                string html = string.Empty;
                try
                {
                    // 1. Descargar HTML de la página actual
                    html = await DescargarHtmlAsync(urlActual, proxy);
                    if (string.IsNullOrWhiteSpace(html))
                    {
                        throw new Exception("El servidor retornó un documento HTML vacío.");
                    }

                    // 2. Analizar si hay firmas de bloqueo o captcha
                    if (html.Contains("px-captcha") || html.Contains("cloudflare") || html.Contains("Verify Your Identity") || html.Contains("Verifica tu identidad"))
                    {
                        resultadoGlobal.RecommendedStrategy = "Cloudflare";
                        throw new Exception("[BLOQUEO / CAPTCHA] Se detectó una firma de bloqueo o captcha en el portal.");
                    }

                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    // 3. Obtener los selectores (usar overrides o los del sitio, o heurísticos)
                    var containerSelector = overrideContainer ?? sitio.SelectorProductoXPath;
                    var nombreSelector = overrideNombre ?? sitio.SelectorNombreXPath;
                    var precioSelector = overridePrecio ?? sitio.SelectorPrecioXPath;
                    var linkSelector = overrideLink ?? "a"; // Por defecto un tag 'a'
                    var imagenSelector = overrideImagen ?? sitio.SelectorImagenXPath;
                    var paginadorSelector = overridePaginador ?? sitio.SelectorStockXPath; // Nota: Reutilizamos campos auxiliares o pasamos explícitamente

                    // Si no hay selector de contenedor configurado, ejecutar heurística para auto-detectar
                    if (string.IsNullOrWhiteSpace(containerSelector))
                    {
                        var heuristicSelectors = DetectSelectorsHeuristic(doc, sitio.UrlBase);
                        if (heuristicSelectors != null)
                        {
                            containerSelector = heuristicSelectors.Container;
                            nombreSelector = heuristicSelectors.Nombre;
                            precioSelector = heuristicSelectors.Precio;
                            linkSelector = heuristicSelectors.Link;
                            imagenSelector = heuristicSelectors.Imagen;
                            paginadorSelector = heuristicSelectors.Paginador;

                            // Guardar selectores aprendidos en el resultado de esta corrida
                            resultadoGlobal.LearnedSelectorProducto = containerSelector;
                            resultadoGlobal.LearnedSelectorNombre = nombreSelector;
                            resultadoGlobal.LearnedSelectorPrecio = precioSelector;
                            resultadoGlobal.LearnedSelectorLink = linkSelector;
                            resultadoGlobal.LearnedSelectorImagen = imagenSelector;
                            resultadoGlobal.LearnedSelectorPaginador = paginadorSelector;
                        }
                        else
                        {
                            throw new Exception("No se pudo deducir heurísticamente la cuadrícula de productos. Configure los selectores de forma manual.");
                        }
                    }

                    // 4. Extraer productos de la página actual
                    var productNodes = doc.DocumentNode.SelectNodes(containerSelector);
                    if (productNodes == null || !productNodes.Any())
                    {
                        throw new Exception($"No se encontraron nodos de producto utilizando el selector: '{containerSelector}'");
                    }

                    int productosEnPagina = 0;
                    int cardIndex = 0;
                    foreach (var cardNode in productNodes)
                    {
                        cardIndex++;
                        string? link = null;
                        string? sku = null;
                        try
                        {
                            // Extraer Enlace (con soporte expandido para SPAs y click-tracking)
                            if (linkSelector == "self" || linkSelector == ".")
                            {
                                link = GetFirstValidAttribute(cardNode, "href", "data-href", "data-url", "data-permalink", "data-path", "pathname");
                            }
                            else if (!string.IsNullOrWhiteSpace(linkSelector))
                            {
                                var linkNode = cardNode.SelectSingleNode(linkSelector);
                                if (linkNode != null)
                                {
                                    link = GetFirstValidAttribute(linkNode, "href", "data-href", "data-url", "data-permalink", "data-path", "pathname");
                                }
                            }
                            
                            // Si no se obtuvo enlace o no es válido, intentar un fallback buscando en todos los tags 'a'
                            if (string.IsNullOrEmpty(link))
                            {
                                if (cardNode.Name == "a")
                                {
                                    link = GetFirstValidAttribute(cardNode, "href", "data-href", "data-url");
                                }
                                
                                if (string.IsNullOrEmpty(link))
                                {
                                    var allLinks = cardNode.SelectNodes(".//a");
                                    if (allLinks != null)
                                    {
                                        foreach (var aNode in allLinks)
                                        {
                                            var candidate = GetFirstValidAttribute(aNode, "href", "data-href", "data-url", "data-permalink", "data-path", "pathname");
                                            if (!string.IsNullOrEmpty(candidate))
                                            {
                                                link = candidate;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            if (string.IsNullOrEmpty(link))
                            {
                                resultadoGlobal.Diagnosticos.Add($"[Pág {pagina} - Tarjeta #{cardIndex}] Omitida: No se detectó ningún enlace de producto en la tarjeta.");
                                continue;
                            }
                            link = MakeAbsoluteUrl(link, sitio.UrlBase);

                            // Extraer Nombre (con decodificación y limpieza de insignias/ruido de promoción)
                            string nombre = string.Empty;
                            if (!string.IsNullOrWhiteSpace(nombreSelector))
                            {
                                var nombreNode = cardNode.SelectSingleNode(nombreSelector);
                                nombre = ExtraerNombreLimpio(nombreNode);
                            }
                            if (string.IsNullOrEmpty(nombre))
                            {
                                // Fallback a texto del link o primer encabezado
                                var fallbackNameNode = cardNode.SelectSingleNode(".//h3") ?? cardNode.SelectSingleNode(".//h2") ?? cardNode.SelectSingleNode(".//a");
                                nombre = ExtraerNombreLimpio(fallbackNameNode);
                            }
                            if (string.IsNullOrEmpty(nombre))
                            {
                                resultadoGlobal.Diagnosticos.Add($"[Pág {pagina} - Tarjeta #{cardIndex}] Omitida: No se pudo extraer el título del producto. Enlace: {link}");
                                continue;
                            }

                            // Extraer Precio con Heurística de Mínimo Precio (Dual Pricing) y Extracción Estructurada
                            decimal precio = 0;
                            if (!string.IsNullOrWhiteSpace(precioSelector))
                            {
                                var precioNode = cardNode.SelectSingleNode(precioSelector);
                                var precioParsed = ExtraerPrecioEstructurado(precioNode) ?? ParsePrice(precioNode?.InnerText);
                                if (precioParsed.HasValue)
                                {
                                    precio = precioParsed.Value;
                                }
                            }

                            // Si no se obtuvo precio o es 0, intentar buscar el precio mínimo entre los descendientes (heurística de oferta)
                            if (precio == 0)
                            {
                                var posiblesNodosPrecio = cardNode.SelectNodes(".//*[contains(text(), '$') or contains(@class, 'price') or contains(@class, 'precio') or contains(@class, 'amount')]");
                                if (posiblesNodosPrecio != null)
                                {
                                    var preciosEncontrados = new List<decimal>();
                                    foreach (var pNode in posiblesNodosPrecio)
                                    {
                                        if (pNode.ChildNodes.Count(c => c.Name != "#text") > 3) continue;

                                        var parsed = ExtraerPrecioEstructurado(pNode) ?? ParsePrice(pNode.InnerText);
                                        if (parsed.HasValue && parsed.Value > 0)
                                        {
                                            preciosEncontrados.Add(parsed.Value);
                                        }
                                    }

                                    if (preciosEncontrados.Any())
                                    {
                                        var promoNodes = cardNode.SelectNodes(".//*[contains(@class, 'discount') or contains(@class, 'promo') or contains(@class, 'sale') or contains(@class, 'special') or contains(@class, 'efectivo') or contains(@class, 'cash') or contains(@class, 'current')]");
                                        decimal? promoPrecio = null;
                                        if (promoNodes != null)
                                        {
                                            foreach (var pNode in promoNodes)
                                            {
                                                var parsed = ExtraerPrecioEstructurado(pNode) ?? ParsePrice(pNode.InnerText);
                                                if (parsed.HasValue && parsed.Value > 0)
                                                {
                                                    promoPrecio = parsed.Value;
                                                    break; // Usar el primero
                                                }
                                            }
                                        }

                                        precio = promoPrecio ?? preciosEncontrados.Min();
                                    }
                                }
                            }

                            if (precio == 0)
                            {
                                resultadoGlobal.Diagnosticos.Add($"[Pág {pagina} - Tarjeta #{cardIndex}] Aviso: Se extrajo el producto '{nombre}' pero el precio quedó en $0.00. Revisa los selectores.");
                            }

                            // Extraer Imagen (soportando srcset de alta resolución, picture/source y lazy loading)
                            string? imagenUrl = null;
                            if (!string.IsNullOrWhiteSpace(imagenSelector))
                            {
                                var imgNode = cardNode.SelectSingleNode(imagenSelector);
                                if (imgNode != null)
                                {
                                    imagenUrl = ExtraerMejorImagen(imgNode);
                                }
                            }
                            if (string.IsNullOrEmpty(imagenUrl))
                            {
                                var fallbackImgNode = cardNode.SelectSingleNode(".//img");
                                if (fallbackImgNode != null)
                                {
                                    imagenUrl = ExtraerMejorImagen(fallbackImgNode);
                                }
                            }
                            if (!string.IsNullOrEmpty(imagenUrl))
                            {
                                imagenUrl = MakeAbsoluteUrl(imagenUrl, sitio.UrlBase);
                            }
                            else
                            {
                                resultadoGlobal.Diagnosticos.Add($"[Pág {pagina} - Tarjeta #{cardIndex}] Aviso: No se localizó ninguna imagen para el producto '{nombre}'. Enlace: {link}");
                            }

                            // Deducir SKU a partir de la URL del producto de forma heurística
                            sku = DeducirSkuDeUrl(link);

                            resultadoGlobal.Productos.Add(new CategoryProductResult
                            {
                                Nombre = nombre,
                                Precio = precio,
                                UrlProducto = link,
                                ImagenUrl = imagenUrl,
                                Sku = sku
                            });
                            productosEnPagina++;
                        }
                        catch (Exception ex)
                        {
                            resultadoGlobal.Diagnosticos.Add($"[Pág {pagina} - Tarjeta #{cardIndex}] Error crítico al procesar tarjeta: {ex.Message}. Enlace: {link ?? "N/A"}");
                        }
                    }

                    // 5. Detectar URL de la siguiente página (Paginación)
                    string? siguienteUrl = null;
                    if (!string.IsNullOrWhiteSpace(paginadorSelector))
                    {
                        var nextNode = doc.DocumentNode.SelectSingleNode(paginadorSelector);
                        siguienteUrl = nextNode?.Attributes["href"]?.Value ?? nextNode?.GetAttributeValue("href", null);
                    }
                    if (string.IsNullOrEmpty(siguienteUrl))
                    {
                        // Si no hay selector de paginador configurado, usar heurística para auto-detectar
                        siguienteUrl = DetectNextPageUrlHeuristic(doc, urlActual);
                        if (!string.IsNullOrEmpty(siguienteUrl) && string.IsNullOrEmpty(resultadoGlobal.LearnedSelectorPaginador))
                        {
                            // Guardar selector de paginador heurístico aprendido
                            resultadoGlobal.LearnedSelectorPaginador = "//a[@rel='next']"; // El estándar detectado
                        }
                    }

                    if (!string.IsNullOrEmpty(siguienteUrl))
                    {
                        urlActual = MakeAbsoluteUrl(siguienteUrl, sitio.UrlBase);
                        // Breve retraso de 1.2 segundos para ser cortés con el portal
                        await Task.Delay(1200);
                    }
                    else
                    {
                        urlActual = null; // Fin de la paginación
                    }
                }
                catch (Exception ex)
                {
                    resultadoGlobal.ErrorMensaje = $"Fallo en página {pagina}: {ex.Message}";
                    break; // Romper en caso de error crítico de red o de parseo
                }
            }

            resultadoGlobal.PaginasProcesadas = pagina;
            resultadoGlobal.Exitoso = resultadoGlobal.Productos.Any();
            return resultadoGlobal;
        }

        private async Task<string> DescargarHtmlAsync(string url, RegistroProxy? proxy)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Brotli
            };

            if (proxy != null)
            {
                handler.Proxy = new System.Net.WebProxy(proxy.Ip, proxy.Puerto);
                handler.UseProxy = true;
            }

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "es-MX,es;q=0.9,en;q=0.8");

            return await client.GetStringAsync(url);
        }

        public static decimal? ParsePrice(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            try
            {
                // Extraer solo números, comas y puntos
                var clean = Regex.Replace(input, @"[^\d.,]", "").Trim();
                if (string.IsNullOrEmpty(clean)) return null;

                if (clean.Contains(",") && clean.Contains("."))
                {
                    if (clean.LastIndexOf('.') > clean.LastIndexOf(','))
                    {
                        clean = clean.Replace(",", "");
                    }
                    else
                    {
                        clean = clean.Replace(".", "").Replace(",", ".");
                    }
                }
                else if (clean.Contains(",") && !clean.Contains("."))
                {
                    var parts = clean.Split(',');
                    if (parts.Length == 2 && parts[1].Length == 2)
                    {
                        clean = clean.Replace(",", ".");
                    }
                    else
                    {
                        clean = clean.Replace(",", "");
                    }
                }

                if (decimal.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
                {
                    return price;
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static string DeducirSkuDeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            try
            {
                // 1. Intentar buscar en parámetros de consulta comunes (ej: ?sku=12345, &productId=998877)
                var queryMatch = Regex.Match(url, @"[?&](?:sku|id|prod|product|code|productId|productCode|spu|item|itemId)=(\w{4,20})", RegexOptions.IgnoreCase);
                if (queryMatch.Success)
                {
                    return queryMatch.Groups[1].Value.ToUpper();
                }

                // 2. Intentar buscar patrones comunes de IDs o SKUs en la URL (números de 4 a 15 dígitos al final de un segmento)
                // Ej: /p/1264415, -1264415.html, /1264415/
                var match = Regex.Match(url, @"[/-](\d{4,15})(?:\.html|\?|/|$)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }

                // 3. Intentar buscar identificador tipo mercado libre o similar (ej: MLM-1234567890 o MLM1234567890)
                var matchML = Regex.Match(url, @"(ML[A-Z]-\d{7,15}|ML[A-Z]\d{7,15})", RegexOptions.IgnoreCase);
                if (matchML.Success)
                {
                    return matchML.Groups[1].Value.ToUpper();
                }
            }
            catch { /* ignore */ }

            // Fallback: Generar un hash determinista de la URL de 8 caracteres para asegurar un SKU no nulo
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url));
            var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            return "SKU-" + hashString.Substring(0, 8).ToUpper();
        }

        private static string? DetectNextPageUrlHeuristic(HtmlDocument doc, string currentUrl)
        {
            var nextKeywords = new[] { "siguiente", "next", "sig", "chevron", ">", "»", "sig >" };
            var links = doc.DocumentNode.SelectNodes("//a[@href]");
            if (links == null) return null;

            foreach (var link in links)
            {
                var text = link.InnerText.ToLower().Trim();
                var rel = link.GetAttributeValue("rel", "").ToLower();
                var className = link.GetAttributeValue("class", "").ToLower();
                var id = link.GetAttributeValue("id", "").ToLower();
                var ariaLabel = link.GetAttributeValue("aria-label", "").ToLower();
                var title = link.GetAttributeValue("title", "").ToLower();

                // 1. Rel="next" es el estándar de SEO, máxima prioridad
                if (rel == "next" || rel.Contains("siguiente"))
                {
                    var href = link.GetAttributeValue("href", null);
                    if (!string.IsNullOrEmpty(href)) return href;
                }

                // 2. Comprobar texto, aria-label o título
                bool matchesKeyword = nextKeywords.Any(k => 
                    text == k || 
                    text.Contains("siguiente") || 
                    text.Contains("next") ||
                    ariaLabel.Contains("siguiente") || 
                    ariaLabel.Contains("next") || 
                    ariaLabel.Contains("page-next") ||
                    title.Contains("siguiente") || 
                    title.Contains("next")
                );

                // 3. Comprobar clases o IDs del enlace o sus hijos inmediatos
                bool matchesClassOrId = className.Contains("next") || 
                                        id.Contains("next") || 
                                        className.Contains("siguiente") ||
                                        link.SelectSingleNode(".//*[contains(@class, 'next') or contains(@class, 'siguiente') or contains(@class, 'chevron-right') or contains(@class, 'arrow-right')]") != null;

                if (matchesKeyword || matchesClassOrId)
                {
                    // Evitar falsos positivos como "volver a ver productos" o enlaces de detalles
                    if (!text.Contains("producto") && !text.Contains("artículo") && !text.Contains("detalle"))
                    {
                        var href = link.GetAttributeValue("href", null);
                        if (!string.IsNullOrEmpty(href)) return href;
                    }
                }
            }

            return null;
        }

        public class HeuristicSelectors
        {
            public string Container { get; set; } = string.Empty;
            public string Nombre { get; set; } = string.Empty;
            public string Precio { get; set; } = string.Empty;
            public string Link { get; set; } = string.Empty;
            public string Imagen { get; set; } = string.Empty;
            public string Paginador { get; set; } = string.Empty;
        }

        public static HeuristicSelectors? DetectSelectorsHeuristic(HtmlDocument doc, string baseUrl)
        {
            // Heurística avanzada de detección por repetición de estructura DOM (libre de clases fijas)
            try
            {
                var allNodes = doc.DocumentNode.Descendants().ToList();
                var candidates = new List<HtmlNode>();

                // 1. Identificar nodos del DOM que contienen individualmente la estructura básica de un producto
                foreach (var node in allNodes)
                {
                    if (node.Name == "#text" || node.Name == "#comment" || EsNodoDeRuido(node)) continue;

                    // Debe tener un enlace (o ser uno mismo)
                    var hasLink = node.Name == "a" || 
                                  node.Attributes.Contains("href") || 
                                  node.Attributes.Contains("data-href") || 
                                  node.Attributes.Contains("data-url") || 
                                  node.SelectSingleNode(".//*[@href or @data-href or @data-url or @data-permalink or @data-path]") != null;

                    // Debe tener al menos una imagen (o ser una o tener estilo de fondo)
                    var hasImg = node.Name == "img" || 
                                 node.SelectSingleNode(".//img | .//picture | .//source") != null ||
                                 (node.GetAttributeValue("style", "").Contains("background-image") || node.SelectSingleNode(".//*[contains(@style, 'background-image')]") != null);

                    // Debe tener algún precio (detectado estructuradamente, con signo de pesos o clases/IDs de precio)
                    var hasPrice = node.SelectSingleNode(".//*[contains(text(), '$') or contains(text(), 'MXN') or contains(text(), 'USD') or contains(@class, 'price') or contains(@class, 'precio') or contains(@class, 'amount') or contains(@class, 'money') or contains(@class, 'fraction') or contains(@class, 'cost') or contains(@class, 'costo')]") != null;

                    if (hasLink && hasImg && hasPrice)
                    {
                        candidates.Add(node);
                    }
                }

                if (!candidates.Any()) return null;

                // 2. Filtrar para quedarnos únicamente con los candidatos "hoja" (los contenedores más internos)
                var leafCandidates = new List<HtmlNode>();
                foreach (var candidate in candidates)
                {
                    bool hasDescendantCandidate = candidate.Descendants().Any(d => candidates.Contains(d));
                    if (!hasDescendantCandidate)
                    {
                        leafCandidates.Add(candidate);
                    }
                }

                if (!leafCandidates.Any()) return null;

                // 3. Agrupar los candidatos usando anclas estables y caminos estructurales relativos
                var groups = leafCandidates
                    .Select(n => {
                        var (anchor, path) = ObtenerAnclaYCamino(n);
                        return new { Node = n, Key = new StructuralGroupKey { AnchorNode = anchor, RelativePath = path } };
                    })
                    .GroupBy(x => x.Key)
                    .Select(g => new { Key = g.Key, Children = g.Select(x => x.Node).ToList(), Count = g.Count() })
                    .Where(g => g.Count >= 3) // Al menos 3 repeticiones hermanas para ser considerado un lister
                    .OrderByDescending(g => g.Count)
                    .ToList();

                if (!groups.Any()) return null;

                var winningGroup = groups.First();
                var anchorNode = winningGroup.Key.AnchorNode;
                var relativePath = winningGroup.Key.RelativePath;

                // 4. Generar el selector de contenedor robusto del ancla + el camino relativo estructural
                string containerSelector;
                if (anchorNode != null)
                {
                    var anchorId = anchorNode.GetAttributeValue("id", "").Trim();
                    if (!string.IsNullOrEmpty(anchorId) && !Regex.IsMatch(anchorId, @"\d{4,}") && !anchorId.Contains("ember") && !anchorId.Contains("ng-"))
                    {
                        containerSelector = $"//*[@id='{anchorId}']";
                    }
                    else
                    {
                        // Generar ruta estructural simple hasta el nodo ancla
                        var pathElements = new List<string>();
                        var cur = anchorNode;
                        while (cur != null && cur.Name != "#document" && cur.Name != "html")
                        {
                            pathElements.Insert(0, cur.Name);
                            cur = cur.ParentNode;
                        }
                        containerSelector = "//" + string.Join("/", pathElements);
                    }
                    
                    if (!string.IsNullOrEmpty(relativePath))
                    {
                        containerSelector += "/" + relativePath;
                    }
                }
                else
                {
                    return null;
                }

                // Filtro de validación estructural
                containerSelector += "[.//a and .//img]";

                // 5. Deducir selectores relativos robustos y libres de clases fijas
                var selectors = new HeuristicSelectors
                {
                    Container = containerSelector,
                    Nombre = ".//h3[1] | .//h2[1] | .//h4[1] | .//h5[1] | .//*[contains(@class, 'name') or contains(@class, 'title') or contains(@class, 'titulo')][not(self::a) or text()][1] | .//a[text() and @href][1] | .//a[@href][1]",
                    Precio = ".//*[contains(@class, 'discount') or contains(@class, 'promo') or contains(@class, 'sale') or contains(@class, 'special') or contains(@class, 'current') or contains(@class, 'price') or contains(@class, 'precio') or contains(@class, 'amount') or contains(@class, 'money')][contains(text(), '$') or .//*[contains(text(), '$')]][1] | .//*[contains(text(), '$')][1] | .//*[contains(@class, 'price') or contains(@class, 'precio')][1]",
                    Link = ".//a[contains(@class, 'product') or contains(@class, 'item') or contains(@class, 'name') or contains(@class, 'title') or contains(@class, 'link')][1] | .//a[@href][1]",
                    Imagen = ".//img[contains(@class, 'product') or contains(@class, 'item') or contains(@class, 'main') or contains(@class, 'primary') or contains(@class, 'image') or contains(@class, 'img')][1] | .//img[1] | .//*[contains(@style, 'background-image')][1]",
                    Paginador = "//a[@rel='next'][1] | //a[contains(@class, 'next') or contains(@class, 'siguiente') or contains(@aria-label, 'siguiente') or contains(@title, 'siguiente') or contains(text(), 'Siguiente') or contains(text(), 'Next')][1]"
                };

                return selectors;
            }
            catch
            {
                return null;
            }
        }

        private static string GenerarRutaEstructuralSinClases(HtmlNode node)
        {
            var pathElements = new List<string>();
            var current = node;
            int levels = 0;
            
            // Subir hasta 4 niveles o hasta encontrar un ancestro con un ID estable
            while (current != null && current.Name != "#document" && current.Name != "html" && levels < 4)
            {
                var id = current.GetAttributeValue("id", "").Trim();
                // Si tiene un ID que parece estable (no autogenerado con números aleatorios)
                if (!string.IsNullOrEmpty(id) && !Regex.IsMatch(id, @"\d{4,}") && !id.Contains("ember") && !id.Contains("ng-") && !id.Contains("layout"))
                {
                    pathElements.Insert(0, $"*[@id='{id}']");
                    break;
                }
                
                pathElements.Insert(0, current.Name);
                current = current.ParentNode;
                levels++;
            }

            var xpath = "//" + string.Join("/", pathElements);
            
            // Filtro de validación estructural para asegurar que los nodos seleccionados sean cards válidas
            xpath += "[.//a and .//img]";
            return xpath;
        }

        private static bool EsNodoDeRuido(HtmlNode node)
        {
            var current = node.ParentNode;
            while (current != null)
            {
                var name = current.Name.ToLower();
                if (name == "head" || name == "script" || name == "style" || name == "noscript" || 
                    name == "svg" || name == "iframe" || name == "header" || name == "footer" || 
                    name == "nav" || name == "aside")
                {
                    return true;
                }

                var classVal = current.GetAttributeValue("class", "").ToLower();
                var idVal = current.GetAttributeValue("id", "").ToLower();
                if (classVal.Contains("header") || classVal.Contains("footer") || classVal.Contains("nav") || classVal.Contains("menu") ||
                    idVal.Contains("header") || idVal.Contains("footer") || idVal.Contains("nav") || idVal.Contains("menu"))
                {
                    return true;
                }

                current = current.ParentNode;
            }
            return false;
        }

        private static string ObtenerMejorClaseParaFirma(string classAttr)
        {
            if (string.IsNullOrWhiteSpace(classAttr)) return string.Empty;

            var classes = classAttr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(c => c.Trim())
                                   .Where(c => !string.IsNullOrEmpty(c))
                                   .ToList();

            if (!classes.Any()) return string.Empty;

            // 1. Filtrar clases de sistema, layout, frameworks y utilidades
            var clasesFiltradas = classes.Where(c => 
                !c.StartsWith("ng-") && 
                !c.StartsWith("mat-") && 
                !c.StartsWith("mdc-") &&
                !c.StartsWith("col-") &&
                !c.StartsWith("row-") &&
                !c.StartsWith("flex-") &&
                !c.StartsWith("grid-") &&
                !c.Equals("row") &&
                !c.Equals("flex") &&
                !c.Equals("grid") &&
                !c.Equals("container") &&
                !c.Equals("ng-star-inserted") &&
                !c.Contains("bootstrap") &&
                !Regex.IsMatch(c, @"^[pmgtbxy]-[0-9]+$") && // e.g., p-4, m-2, g-3
                !Regex.IsMatch(c, @"^(w|h|col|row|order|flex|grid|justify|align|self|items|content|text|bg|border|rounded|shadow|opacity|z|transition|duration|delay|ease|overflow|cursor|pointer-events|select|resize|border-style|border-width|border-color|divide|ring|outline)-") && // utilidades tailwind
                !Regex.IsMatch(c, @"^(sm|md|lg|xl|2xl|focus|hover|active|disabled|visited|dark|motion-reduce|motion-safe|first|last|odd|even):") // modificadores tailwind
            ).ToList();

            if (!clasesFiltradas.Any())
            {
                // Fallback: clases no-Angular / no-Material
                var fallbackClass = classes.FirstOrDefault(c => !c.Contains("ng-") && !c.Contains("mat-") && !c.Contains("mdc-"));
                return fallbackClass ?? string.Empty;
            }

            // 2. Priorizar clases semánticas de e-commerce
            var keywords = new[] { "product", "producto", "item", "card", "tarjeta", "lister", "tile", "entry", "article", "result", "resultado" };
            foreach (var kw in keywords)
            {
                var match = clasesFiltradas.FirstOrDefault(c => c.Contains(kw, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            // 3. Retornar la primera clase filtrada
            return clasesFiltradas.First();
        }

        private static string? GetFirstNonEmptyAttribute(HtmlNode node, params string[] attributeNames)
        {
            foreach (var attr in attributeNames)
            {
                var val = node.GetAttributeValue(attr, null)?.Trim();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }
            return null;
        }

        public static decimal? ExtraerPrecioEstructurado(HtmlNode priceNode)
        {
            if (priceNode == null) return null;

            // Excluir elementos tachados (precios anteriores/lista)
            var cleanPriceNode = priceNode.CloneNode(true);
            var struckNodes = cleanPriceNode.SelectNodes(".//*[contains(@style, 'line-through') or name()='del' or name()='s' or contains(@class, 'old') or contains(@class, 'original') or contains(@class, 'list-price')]");
            if (struckNodes != null)
            {
                foreach (var sNode in struckNodes)
                {
                    try { sNode.Remove(); } catch { }
                }
            }

            // Excluir elementos de ruido promocional (porcentaje de descuento, insignias de envío, etc.) que puedan interferir con los dígitos del precio
            var promoNoiseNodes = cleanPriceNode.SelectNodes(".//*[contains(@class, 'promo') or contains(@class, 'discount') or contains(@class, 'badge') or contains(@class, 'tag') or contains(@class, 'offer') or contains(@class, 'sale') or contains(@class, 'shipping') or contains(@class, 'envio') or contains(@class, 'ahorro') or contains(@class, 'flag') or contains(text(), '%') or contains(text(), 'OFF')]");
            if (promoNoiseNodes != null)
            {
                foreach (var pNode in promoNoiseNodes)
                {
                    try { pNode.Remove(); } catch { }
                }
            }

            // 1. Intentar buscar por clases explícitas
            var wholeNode = cleanPriceNode.SelectSingleNode(".//*[contains(@class, 'fraction') or contains(@class, 'whole') or contains(@class, 'integer') or contains(@class, 'price-whole') or contains(@class, 'main-price') or contains(@class, 'amount') or contains(@class, 'price-amount')]");
            var centsNode = cleanPriceNode.SelectSingleNode(".//*[contains(@class, 'cents') or contains(@class, 'decimal') or contains(@class, 'fraction-cents') or contains(@class, 'price-cents') or contains(@class, 'centavos')]");

            // Fallback: Si no se encuentran por clases explícitas, buscar todos los elementos hoja con números
            if (wholeNode == null || centsNode == null)
            {
                var leafDigitNodes = cleanPriceNode.Descendants()
                    .Where(n => !n.HasChildNodes && Regex.IsMatch(n.InnerText, @"\d+"))
                    .ToList();

                if (leafDigitNodes.Count >= 2)
                {
                    var firstText = leafDigitNodes[0].InnerText.Trim();
                    var secondText = leafDigitNodes[1].InnerText.Trim();
                    
                    var firstDigits = Regex.Replace(firstText, @"\D", "");
                    var secondDigits = Regex.Replace(secondText, @"\D", "");

                    if (!string.IsNullOrEmpty(firstDigits) && !string.IsNullOrEmpty(secondDigits) && secondDigits.Length <= 2)
                    {
                        wholeNode = leafDigitNodes[0];
                        centsNode = leafDigitNodes[1];
                    }
                }
            }

            // Si el nodo de centavos es exactamente el mismo que el entero, descartarlo
            if (wholeNode != null && centsNode != null && wholeNode.XPath == centsNode.XPath)
            {
                centsNode = null;
            }

            if (wholeNode != null)
            {
                var wholeText = wholeNode.InnerText.Trim();
                var centsText = centsNode != null ? centsNode.InnerText.Trim() : "00";
                
                wholeText = Regex.Replace(wholeText, @"[^\d.,]", "");
                centsText = Regex.Replace(centsText, @"\D", "");
                
                if (centsText.Length == 1) centsText += "0";
                if (centsText.Length > 2) centsText = centsText.Substring(0, 2);
                if (string.IsNullOrEmpty(centsText)) centsText = "00";

                var combined = $"{wholeText}.{centsText}";
                return ParsePrice(combined);
            }

            return null;
        }

        public static string? ExtraerMejorImagen(HtmlNode imgNode)
        {
            if (imgNode == null) return null;

            // 1. Si es un elemento 'picture' o el padre es 'picture', buscar en los elementos 'source'
            var pictureNode = imgNode.Name == "picture" ? imgNode : (imgNode.ParentNode?.Name == "picture" ? imgNode.ParentNode : null);
            if (pictureNode != null)
            {
                var sources = pictureNode.SelectNodes(".//source[@srcset]");
                if (sources != null)
                {
                    foreach (var source in sources)
                    {
                        var sourceSrcset = source.GetAttributeValue("srcset", null);
                        if (!string.IsNullOrWhiteSpace(sourceSrcset))
                        {
                            var url = ObtenerUrlDeMayorResolucion(sourceSrcset);
                            if (!string.IsNullOrEmpty(url)) return url;
                        }
                    }
                }
            }

            // 2. Intentar buscar en data-srcset o srcset del propio nodo
            var srcset = imgNode.GetAttributeValue("data-srcset", null) ?? imgNode.GetAttributeValue("srcset", null);
            if (!string.IsNullOrWhiteSpace(srcset))
            {
                var url = ObtenerUrlDeMayorResolucion(srcset);
                if (!string.IsNullOrEmpty(url)) return url;
            }

            // 3. Fallback a atributos de fuente tradicionales de lazy loading
            var imagenUrl = GetFirstNonEmptyAttribute(imgNode, "data-src", "data-lazy-src", "data-original", "data-zoom-image", "data-responsive-image", "data-img-src", "src");

            // Si el enlace apunta a un marcador de posición transparente o spinner, buscar el real en data
            if (imagenUrl != null && (imagenUrl.StartsWith("data:image") || imagenUrl.Contains("blank") || imagenUrl.Contains("placeholder") || imagenUrl.Contains("transparent")))
            {
                var ds = GetFirstNonEmptyAttribute(imgNode, "data-src", "data-lazy-src", "data-original", "data-zoom-image", "data-responsive-image", "data-img-src");
                if (!string.IsNullOrEmpty(ds)) imagenUrl = ds;
            }

            // 4. Fallback a estilo de imagen de fondo (background-image) en inline CSS
            if (string.IsNullOrEmpty(imagenUrl))
            {
                var style = imgNode.GetAttributeValue("style", null);
                if (!string.IsNullOrWhiteSpace(style))
                {
                    var match = Regex.Match(style, @"background-image\s*:\s*url\s*\(\s*['""]?(.*?)['""]?\s*\)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        imagenUrl = match.Groups[1].Value;
                    }
                    else
                    {
                        var matchBg = Regex.Match(style, @"background\s*:\s*[^;]*url\s*\(\s*['""]?(.*?)['""]?\s*\)", RegexOptions.IgnoreCase);
                        if (matchBg.Success)
                        {
                            imagenUrl = matchBg.Groups[1].Value;
                        }
                    }
                }
            }

            return imagenUrl;
        }

        private static string? ObtenerUrlDeMayorResolucion(string srcset)
        {
            try
            {
                var parts = srcset.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (!parts.Any()) return null;

                string? bestUrl = null;
                double maxRes = 0;

                foreach (var part in parts)
                {
                    var subParts = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (subParts.Length == 0) continue;

                    var url = subParts[0];
                    double res = 1; // Resolución base por defecto

                    if (subParts.Length > 1)
                    {
                        var descriptor = subParts[1].ToLower().Trim();
                        if (descriptor.EndsWith("w"))
                        {
                            if (double.TryParse(descriptor.TrimEnd('w'), out var width))
                            {
                                res = width;
                            }
                        }
                        else if (descriptor.EndsWith("x"))
                        {
                            if (double.TryParse(descriptor.TrimEnd('x'), out var density))
                            {
                                res = density * 1000; // Normalizar para comparar con anchos
                            }
                        }
                    }

                    if (res > maxRes || bestUrl == null)
                    {
                        maxRes = res;
                        bestUrl = url;
                    }
                }

                return bestUrl;
            }
            catch { /* ignore */ }
            return null;
        }

        public static string ExtraerNombreLimpio(HtmlNode nombreNode)
        {
            if (nombreNode == null) return string.Empty;

            // Clonar el nodo para no alterar el DOM original del documento
            var clone = nombreNode.CloneNode(true);
            
            // Buscar y remover elementos de ruido internos (como etiquetas de descuento, insignias, "Envío Gratis", etc.)
            var ruidoNodes = clone.SelectNodes(".//*[contains(@class, 'promo') or contains(@class, 'discount') or contains(@class, 'badge') or contains(@class, 'tag') or contains(@class, 'offer') or contains(@class, 'sale') or contains(@class, 'shipping') or contains(@class, 'envio') or contains(@class, 'ahorro') or contains(@class, 'flag') or contains(text(), '%') or contains(text(), 'OFF')]");
            if (ruidoNodes != null)
            {
                foreach (var rNode in ruidoNodes)
                {
                    try
                    {
                        rNode.Remove();
                    }
                    catch { /* ignore */ }
                }
            }

            var text = HtmlEntity.DeEntitize(clone.InnerText).Trim();
            // Normalizar espacios en blanco múltiples
            text = Regex.Replace(text, @"\s+", " ").Trim();
            
            // Remover ruido de texto plano común de promociones y envíos
            var cleanText = text;
            var patterns = new[]
            {
                @"\b\d+\s*%\s*OFF\b",
                @"\b(?:envío|envio)\s+gratis\b",
                @"\bfree\s+shipping\b",
                @"\b\d+\s*(?:msi|meses\s+sin\s+intereses)\b",
                @"\bllega\s+mañana\b",
                @"\b(?:oferta|descuento|ahorro|promoción|promocion)\b"
            };

            foreach (var pattern in patterns)
            {
                cleanText = Regex.Replace(cleanText, pattern, "", RegexOptions.IgnoreCase).Trim();
            }
            
            // Limpiar guiones o barras huérfanas al final del texto debido a la limpieza
            cleanText = Regex.Replace(cleanText, @"\s*[-|•/]\s*$", "").Trim();
            // Normalizar múltiples espacios nuevamente
            cleanText = Regex.Replace(cleanText, @"\s+", " ").Trim();
            
            return cleanText;
        }

        private static string MakeAbsoluteUrl(string url, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            url = url.Trim();
            if (url.StartsWith("//"))
            {
                return "https:" + url;
            }
            if (url.StartsWith("/"))
            {
                try
                {
                    var uri = new Uri(baseUrl);
                    return $"{uri.Scheme}://{uri.Host}{url}";
                }
                catch
                {
                    return url;
                }
            }
            return url;
        }

        public static bool EsEnlaceValido(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            url = url.Trim().ToLower();
            return !url.StartsWith("javascript:") && 
                   url != "#" && 
                   url != "/" && 
                   !url.StartsWith("tel:") && 
                   !url.StartsWith("mailto:");
        }

        private static string? GetFirstValidAttribute(HtmlNode node, params string[] attributeNames)
        {
            foreach (var attr in attributeNames)
            {
                var val = node.GetAttributeValue(attr, null)?.Trim();
                if (EsEnlaceValido(val))
                {
                    return val;
                }
            }
            return null;
        }

        private static (HtmlNode Anchor, string Path) ObtenerAnclaYCamino(HtmlNode node)
        {
            var current = node;
            var pathElements = new List<string>();
            int levels = 0;
            
            while (current.ParentNode != null && current.Name != "#document" && current.Name != "html" && current.Name != "body" && levels < 5)
            {
                var id = current.GetAttributeValue("id", "").Trim();
                if (!string.IsNullOrEmpty(id) && !Regex.IsMatch(id, @"\d{4,}") && !id.Contains("ember") && !id.Contains("ng-") && !id.Contains("layout"))
                {
                    break;
                }
                pathElements.Insert(0, current.Name);
                current = current.ParentNode;
                levels++;
            }
            
            return (current, string.Join("/", pathElements));
        }

        internal class StructuralGroupKey
        {
            public HtmlNode? AnchorNode { get; set; }
            public string RelativePath { get; set; } = string.Empty;

            public override bool Equals(object? obj)
            {
                if (obj is StructuralGroupKey other)
                {
                    return AnchorNode == other.AnchorNode && RelativePath == other.RelativePath;
                }
                return false;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(AnchorNode, RelativePath);
            }
        }
    }
}
