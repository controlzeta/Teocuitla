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
                    foreach (var cardNode in productNodes)
                    {
                        try
                        {
                            // Extraer Enlace
                            string? link = null;
                            if (linkSelector == "self" || linkSelector == ".")
                            {
                                link = GetFirstNonEmptyAttribute(cardNode, "href");
                            }
                            else if (!string.IsNullOrWhiteSpace(linkSelector))
                            {
                                var linkNode = cardNode.SelectSingleNode(linkSelector);
                                if (linkNode != null)
                                {
                                    link = GetFirstNonEmptyAttribute(linkNode, "href");
                                }
                            }
                            
                            // Si no se obtuvo enlace, intentar un fallback rápido al primer tag 'a'
                            if (string.IsNullOrEmpty(link))
                            {
                                var fallbackLinkNode = cardNode.Name == "a" ? cardNode : cardNode.SelectSingleNode(".//a[@href]");
                                if (fallbackLinkNode != null)
                                {
                                    link = GetFirstNonEmptyAttribute(fallbackLinkNode, "href");
                                }
                            }

                            if (string.IsNullOrEmpty(link)) continue;
                            link = MakeAbsoluteUrl(link, sitio.UrlBase);

                            // Extraer Nombre (con decodificación completa de entidades HTML)
                            string nombre = string.Empty;
                            if (!string.IsNullOrWhiteSpace(nombreSelector))
                            {
                                var nombreNode = cardNode.SelectSingleNode(nombreSelector);
                                nombre = nombreNode != null ? HtmlEntity.DeEntitize(nombreNode.InnerText).Trim() : string.Empty;
                            }
                            if (string.IsNullOrEmpty(nombre))
                            {
                                // Fallback a texto del link o primer encabezado
                                var fallbackNameNode = cardNode.SelectSingleNode(".//h3") ?? cardNode.SelectSingleNode(".//h2") ?? cardNode.SelectSingleNode(".//a");
                                nombre = fallbackNameNode != null ? HtmlEntity.DeEntitize(fallbackNameNode.InnerText).Trim() : string.Empty;
                            }
                            if (string.IsNullOrEmpty(nombre)) continue;

                            // Extraer Precio con Heurística de Mínimo Precio (Dual Pricing)
                            decimal precio = 0;
                            if (!string.IsNullOrWhiteSpace(precioSelector))
                            {
                                var precioNode = cardNode.SelectSingleNode(precioSelector);
                                var precioTexto = precioNode?.InnerText;
                                var precioParsed = ParsePrice(precioTexto);
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
                                        // Ignorar nodos que son demasiado grandes o contienen demasiada estructura
                                        if (pNode.ChildNodes.Count(c => c.Name != "#text") > 3) continue;

                                        var txt = pNode.InnerText;
                                        var parsed = ParsePrice(txt);
                                        if (parsed.HasValue && parsed.Value > 0)
                                        {
                                            preciosEncontrados.Add(parsed.Value);
                                        }
                                    }

                                    if (preciosEncontrados.Any())
                                    {
                                        // Buscar si hay alguna etiqueta que indique promoción explícita
                                        var promoNodes = cardNode.SelectNodes(".//*[contains(@class, 'discount') or contains(@class, 'promo') or contains(@class, 'sale') or contains(@class, 'special') or contains(@class, 'efectivo') or contains(@class, 'cash') or contains(@class, 'current')]");
                                        decimal? promoPrecio = null;
                                        if (promoNodes != null)
                                        {
                                            foreach (var pNode in promoNodes)
                                            {
                                                var parsed = ParsePrice(pNode.InnerText);
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

                            // Extraer Imagen (soportando técnicas de Lazy Loading de PWAs como Liverpool y Costco)
                            string? imagenUrl = null;
                            if (!string.IsNullOrWhiteSpace(imagenSelector))
                            {
                                var imgNode = cardNode.SelectSingleNode(imagenSelector);
                                if (imgNode != null)
                                {
                                    // Primero intentar atributos de carga perezosa de alta resolución
                                    imagenUrl = GetFirstNonEmptyAttribute(imgNode, "data-src", "data-lazy-src", "data-original", "src", "href");

                                    // Si el enlace apunta a un marcador de posición transparente de 1x1 o spinner, buscar el real
                                    if (imagenUrl != null && (imagenUrl.StartsWith("data:image") || imagenUrl.Contains("blank") || imagenUrl.Contains("placeholder") || imagenUrl.Contains("transparent")))
                                    {
                                        var ds = GetFirstNonEmptyAttribute(imgNode, "data-src", "data-lazy-src", "data-original");
                                        if (!string.IsNullOrEmpty(ds)) imagenUrl = ds;
                                    }
                                }
                            }
                            if (string.IsNullOrEmpty(imagenUrl))
                            {
                                var fallbackImgNode = cardNode.SelectSingleNode(".//img");
                                if (fallbackImgNode != null)
                                {
                                    imagenUrl = GetFirstNonEmptyAttribute(fallbackImgNode, "data-src", "data-lazy-src", "src");

                                    if (imagenUrl != null && (imagenUrl.StartsWith("data:image") || imagenUrl.Contains("blank") || imagenUrl.Contains("placeholder") || imagenUrl.Contains("transparent")))
                                    {
                                        var ds = GetFirstNonEmptyAttribute(fallbackImgNode, "data-src", "data-lazy-src");
                                        if (!string.IsNullOrEmpty(ds)) imagenUrl = ds;
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(imagenUrl))
                            {
                                imagenUrl = MakeAbsoluteUrl(imagenUrl, sitio.UrlBase);
                            }

                            // Deducir SKU a partir de la URL del producto de forma heurística
                            string sku = DeducirSkuDeUrl(link);

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
                        catch
                        {
                            // Ignorar errores individuales en tarjetas malformadas de la cuadrícula
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

        private static HeuristicSelectors? DetectSelectorsHeuristic(HtmlDocument doc, string baseUrl)
        {
            // Heurística de detección de listado de productos analizando el DOM
            try
            {
                // 1. Encontrar todos los elementos que parecen precios
                var priceTextNodes = new List<HtmlNode>();
                var allTextNodes = doc.DocumentNode.SelectNodes("//text()");
                if (allTextNodes == null) return null;

                foreach (var node in allTextNodes)
                {
                    // Ignorar nodos en scripts, styles, header, footer y otras áreas de ruido
                    if (EsNodoDeRuido(node)) continue;

                    var text = node.InnerText.Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    // Quitar espaciados múltiples internos y newlines para normalizar
                    var cleanText = Regex.Replace(text, @"\s+", " ").Trim();

                    // Candidato 1: Coincide con patrones monetarios (ej: $1,234.56, MXN $99.00, $15,999)
                    var matchesPricePattern = Regex.IsMatch(cleanText, @"(?:\$|MXN|USD)\s*\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{2})?\b") || 
                                              Regex.IsMatch(cleanText, @"^\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{2})?$");

                    if (matchesPricePattern && text.Any(char.IsDigit))
                    {
                        priceTextNodes.Add(node.ParentNode);
                        continue;
                    }

                    // Candidato 2: El padre contiene clases semánticas de precio y el texto tiene dígitos
                    var parent = node.ParentNode;
                    if (parent != null)
                    {
                        var parentClass = parent.GetAttributeValue("class", "").ToLower();
                        if ((parentClass.Contains("price") || parentClass.Contains("precio") || parentClass.Contains("amount")) && 
                            text.Any(char.IsDigit) && text.Length < 35)
                        {
                            priceTextNodes.Add(parent);
                        }
                    }
                }

                if (priceTextNodes.Count < 3) return null; // Necesitamos al menos 3 elementos para deducir un patrón

                // 2. Para cada precio, subir en el DOM e identificar un contenedor común repetitivo
                var candidatePaths = new Dictionary<string, int>();
                foreach (var priceNode in priceTextNodes)
                {
                    var current = priceNode;
                    int level = 0;
                    while (current != null && level < 6)
                    {
                        // Verificar si este ancestro tiene un tag 'a' (link de producto) y una imagen
                        var hasLink = current.Name == "a" || current.SelectSingleNode(".//a[@href]") != null;
                        var hasImg = current.SelectSingleNode(".//img") != null;
                        
                        // En e-commerce modernos, un producto puede o no tener encabezado h2/h3.
                        // Usamos una verificación de estructura general (título, descripción, o spans de texto)
                        var hasStructure = current.ChildNodes.Count(c => c.Name != "#text") > 2;

                        if (hasLink && hasImg && hasStructure)
                        {
                            var tagName = current.Name;
                            var className = current.GetAttributeValue("class", "").Trim();
                            
                            var isCustomTag = tagName.Contains("-");
                            var isProductCardClass = className.Contains("item", StringComparison.OrdinalIgnoreCase) || 
                                                     className.Contains("product", StringComparison.OrdinalIgnoreCase) || 
                                                     className.Contains("card", StringComparison.OrdinalIgnoreCase) || 
                                                     className.Contains("lister", StringComparison.OrdinalIgnoreCase) ||
                                                     className.Contains("tile", StringComparison.OrdinalIgnoreCase);

                            // Si es un tag personalizado (ej: sip-product-list-item, cx-product-grid-item),
                            // no requiere clase específica ya que la etiqueta misma es sumamente específica.
                            string signature;
                            if (isCustomTag)
                            {
                                signature = $"//{tagName}";
                            }
                            else
                            {
                                var bestClass = ObtenerMejorClaseParaFirma(className);
                                signature = string.IsNullOrEmpty(bestClass)
                                    ? $"//{tagName}"
                                    : $"//{tagName}[contains(@class, '{bestClass}')]";
                            }

                            if (!candidatePaths.ContainsKey(signature))
                            {
                                candidatePaths[signature] = 0;
                            }

                            // Sistema de scoring ponderado
                            int weight = 1;
                            if (isCustomTag) weight += 4;
                            if (isProductCardClass) weight += 3;

                            candidatePaths[signature] += weight;
                        }

                        current = current.ParentNode;
                        level++;
                    }
                }

                if (!candidatePaths.Any()) return null;

                // Seleccionar el patrón de contenedor más frecuente
                var bestContainerSignature = candidatePaths.OrderByDescending(x => x.Value).First().Key;

                // 3. Deducir selectores relativos de los elementos dentro del contenedor
                var selectors = new HeuristicSelectors
                {
                    Container = bestContainerSignature,
                    Nombre = ".//h3[1] | .//h2[1] | .//h4[1] | .//h5[1] | .//h6[1] | .//*[contains(@class, 'name') or contains(@class, 'title') or contains(@class, 'description') or contains(@class, 'desc') or contains(@class, 'label')][1] | .//a[@href][1]",
                    Precio = ".//*[contains(@class, 'discount') or contains(@class, 'promo') or contains(@class, 'sale') or contains(@class, 'special') or contains(@class, 'efectivo') or contains(@class, 'cash') or contains(@class, 'current')][1] | .//*[contains(@class, 'price')][1] | .//*[contains(@class, 'precio')][1] | .//*[contains(@class, 'amount')][1] | .//*[contains(text(), '$')][1]",
                    Link = ".//a[contains(@class, 'product') or contains(@class, 'item') or contains(@class, 'name') or contains(@class, 'title') or contains(@class, 'thumb')][1] | .//a[@href][1]",
                    Imagen = ".//img[contains(@class, 'product') or contains(@class, 'item') or contains(@class, 'main') or contains(@class, 'primary') or contains(@class, 'thumb')][1] | .//img[1]",
                    Paginador = "//a[@rel='next'][1] | //a[contains(@class, 'next') or contains(@class, 'siguiente')][1] | //a[contains(text(), 'Siguiente') or contains(text(), 'Next')][1]"
                };

                return selectors;
            }
            catch
            {
                return null;
            }
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
            var keywords = new[] { "product", "producto", "item", "card", "tarjeta", "lister", "tile", "entry", "article" };
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
    }
}
    }
}
