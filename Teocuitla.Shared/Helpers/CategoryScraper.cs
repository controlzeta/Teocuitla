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
                                link = cardNode.Attributes["href"]?.Value ?? cardNode.GetAttributeValue("href", null);
                            }
                            else if (!string.IsNullOrWhiteSpace(linkSelector))
                            {
                                var linkNode = cardNode.SelectSingleNode(linkSelector);
                                link = linkNode?.Attributes["href"]?.Value ?? linkNode?.GetAttributeValue("href", null);
                            }
                            
                            // Si no se obtuvo enlace, intentar un fallback rápido al primer tag 'a'
                            if (string.IsNullOrEmpty(link))
                            {
                                var fallbackLinkNode = cardNode.Name == "a" ? cardNode : cardNode.SelectSingleNode(".//a[@href]");
                                link = fallbackLinkNode?.Attributes["href"]?.Value;
                            }

                            if (string.IsNullOrEmpty(link)) continue;
                            link = MakeAbsoluteUrl(link, sitio.UrlBase);

                            // Extraer Nombre
                            string nombre = string.Empty;
                            if (!string.IsNullOrWhiteSpace(nombreSelector))
                            {
                                var nombreNode = cardNode.SelectSingleNode(nombreSelector);
                                nombre = nombreNode?.InnerText?.Trim() ?? string.Empty;
                            }
                            if (string.IsNullOrEmpty(nombre))
                            {
                                // Fallback a texto del link o primer encabezado
                                var fallbackNameNode = cardNode.SelectSingleNode(".//h3") ?? cardNode.SelectSingleNode(".//h2") ?? cardNode.SelectSingleNode(".//a");
                                nombre = fallbackNameNode?.InnerText?.Trim() ?? string.Empty;
                            }
                            if (string.IsNullOrEmpty(nombre)) continue;

                            // Extraer Precio
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

                            // Extraer Imagen
                            string? imagenUrl = null;
                            if (!string.IsNullOrWhiteSpace(imagenSelector))
                            {
                                var imgNode = cardNode.SelectSingleNode(imagenSelector);
                                imagenUrl = imgNode?.Attributes["src"]?.Value 
                                            ?? imgNode?.Attributes["data-src"]?.Value 
                                            ?? imgNode?.Attributes["href"]?.Value;
                            }
                            if (string.IsNullOrEmpty(imagenUrl))
                            {
                                var fallbackImgNode = cardNode.SelectSingleNode(".//img");
                                imagenUrl = fallbackImgNode?.Attributes["src"]?.Value ?? fallbackImgNode?.Attributes["data-src"]?.Value;
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
                // Intentar buscar patrones comunes de IDs o SKUs en la URL (números de 5 a 15 dígitos)
                var match = Regex.Match(url, @"[/-](\d{5,15})(?:\.html|\?|/|$)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }

                // Intentar buscar identificador tipo mercado libre o similar (ej: MLM-1234567890 o MLM1234567890)
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

                if (rel == "next")
                {
                    return link.GetAttributeValue("href", null);
                }

                if (className.Contains("next") || id.Contains("next") || className.Contains("siguiente"))
                {
                    if (!text.Contains("producto") && !text.Contains("artículo"))
                    {
                        return link.GetAttributeValue("href", null);
                    }
                }

                if (nextKeywords.Any(k => text == k || text.Contains("siguiente") || text.Contains("next")))
                {
                    return link.GetAttributeValue("href", null);
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
                    var text = node.InnerText.Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    // Patrón simple de precio: símbolo de pesos/moneda seguido de números
                    if (Regex.IsMatch(text, @"^\$?\s*\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{2})?$") && text.Any(char.IsDigit))
                    {
                        priceTextNodes.Add(node.ParentNode); // Guardar el elemento padre del nodo de texto
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
                        // Verificar si este ancestro tiene un tag 'a' (link de producto) y una imagen o encabezado
                        var hasLink = current.Name == "a" || current.SelectSingleNode(".//a[@href]") != null;
                        var hasImg = current.SelectSingleNode(".//img") != null;
                        var hasHeading = current.SelectSingleNode(".//h3") != null || current.SelectSingleNode(".//h2") != null 
                                         || current.SelectSingleNode(".//span") != null || current.SelectSingleNode(".//div") != null;

                        if (hasLink && hasImg && hasHeading)
                        {
                            // Generar una firma de clase/estructura para este nodo
                            var tagName = current.Name;
                            var className = current.GetAttributeValue("class", "").Trim();
                            var signature = string.IsNullOrEmpty(className) 
                                ? $"//{tagName}" 
                                : $"//{tagName}[contains(@class, '{className.Split(' ').First()}')]";

                            if (!candidatePaths.ContainsKey(signature))
                            {
                                candidatePaths[signature] = 0;
                            }
                            candidatePaths[signature]++;
                        }

                        current = current.ParentNode;
                        level++;
                    }
                }

                if (!candidatePaths.Any()) return null;

                // Seleccionar el patrón de contenedor más frecuente
                var bestContainerSignature = candidatePaths.OrderByDescending(x => x.Value).First().Key;

                // 3. Deducir selectores relativos de los elementos dentro del contenedor
                // Asumimos selectores genéricos y robustos relativos al contenedor
                var selectors = new HeuristicSelectors
                {
                    Container = bestContainerSignature,
                    Nombre = ".//h3[1] | .//h2[1] | .//a[contains(@class, 'title')][1] | .//span[contains(@class, 'title')][1]",
                    Precio = ".//*[contains(text(), '$')][1] | .//*[contains(@class, 'price')][1]",
                    Link = ".//a[@href][1]",
                    Imagen = ".//img[1]",
                    Paginador = "//a[@rel='next'][1] | //a[contains(@class, 'next')][1]"
                };

                return selectors;
            }
            catch
            {
                return null;
            }
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
