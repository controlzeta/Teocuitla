using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using HtmlAgilityPack;

namespace Teocuitla.Shared.Helpers
{
    public class HeuristicResult
    {
        public string? Nombre { get; set; }
        public decimal? Precio { get; set; }
        public bool EnStock { get; set; } = true;
        public string? XPathSugerido { get; set; }
        public string? ImagenUrl { get; set; }
        public string? Sku { get; set; }
        public string? Marca { get; set; }
        public string MetodoDeteccion { get; set; } = "Ninguno";
    }

    public static class HeuristicExtractor
    {
        private static readonly Regex PriceRegex = new Regex(@"\$\s*(\d{1,3}(?:,\d{3})*(?:\.\d{2})?)", RegexOptions.Compiled);

        /// <summary>
        /// Extrae heuristicamente los datos de un producto a partir del codigo HTML de la pagina.
        /// </summary>
        public static HeuristicResult Extract(string html)
        {
            var result = new HeuristicResult();
            if (string.IsNullOrWhiteSpace(html)) return result;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Intentar Nivel 1: Datos Estructurados JSON-LD (Maximo acierto y precision)
            if (TryExtractFromJsonLd(doc, result))
            {
                result.MetodoDeteccion = "JSON-LD (Datos Estructurados)";
            }
            // Intentar Nivel 2: Meta Etiquetas de Catalogo y Compartir (Open Graph)
            else if (TryExtractFromMetaTags(doc, result))
            {
                result.MetodoDeteccion = "Meta Etiquetas (Open Graph)";
            }
            // Intentar Nivel 3: Analisis Semantico Basico del DOM
            else
            {
                ExtractFromDomHeuristics(doc, result);
                result.MetodoDeteccion = "Analisis Semantico DOM (Fallback)";
            }

            // APRENDIZAJE: Si logramos extraer un precio exitosamente pero aun no tenemos un selector (como en JSON-LD o Meta Tags),
            // buscamos en caliente el elemento visual en el DOM que contiene dicho precio y generamos su XPath inteligente.
            if (result.Precio.HasValue && string.IsNullOrEmpty(result.XPathSugerido))
            {
                var priceNode = FindNodeForPrice(doc, result.Precio.Value);
                if (priceNode != null)
                {
                    result.XPathSugerido = GetSmartXPath(priceNode);
                }
            }

            // --- COMPLEMENTAR SKU Y MARCA DESDE EL DOM SI NINGUNO DE LOS ANTERIORES LOS OBTUVO ---
            if (string.IsNullOrEmpty(result.Sku))
            {
                var skuNode = doc.DocumentNode.SelectSingleNode("//meta[@property='product:retailer_item_id']")
                              ?? doc.DocumentNode.SelectSingleNode("//meta[@itemprop='sku']")
                              ?? doc.DocumentNode.SelectSingleNode("//meta[@name='sku']")
                              ?? doc.DocumentNode.SelectSingleNode("//*[contains(@class, 'sku') or contains(@class, 'codigo') or contains(@id, 'sku') or contains(@id, 'codigo')]");
                if (skuNode != null)
                {
                    result.Sku = DataNormalizer.NormalizeSku(skuNode.GetAttributeValue("content", skuNode.InnerText));
                }
            }

            if (string.IsNullOrEmpty(result.Marca) || result.Marca == "Genérica")
            {
                var brandNode = doc.DocumentNode.SelectSingleNode("//meta[@property='product:brand']")
                                ?? doc.DocumentNode.SelectSingleNode("//meta[@itemprop='brand']")
                                ?? doc.DocumentNode.SelectSingleNode("//meta[@name='brand']")
                                ?? doc.DocumentNode.SelectSingleNode("//a[contains(@href, 'vendors') or contains(@href, 'brand') or contains(@class, 'brand') or contains(@class, 'vendor') or contains(@class, 'vendor-name')]");
                if (brandNode != null)
                {
                    result.Marca = DataNormalizer.NormalizeBrand(brandNode.GetAttributeValue("content", brandNode.InnerText));
                }
            }

            if (!string.IsNullOrEmpty(result.ImagenUrl))
            {
                result.ImagenUrl = DataNormalizer.NormalizeImageUrl(DataNormalizer.MakeAbsoluteUrl(result.ImagenUrl, null));
            }

            return result;
        }

        private static bool TryExtractFromJsonLd(HtmlDocument doc, HeuristicResult result)
        {
            var scriptNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
            if (scriptNodes == null) return false;

            foreach (var node in scriptNodes)
            {
                try
                {
                    var jsonText = node.InnerText.Trim();
                    using var jsonDoc = JsonDocument.Parse(jsonText);
                    var root = jsonDoc.RootElement;

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in root.EnumerateArray())
                        {
                            if (ParseProductElement(elem, result)) return true;
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (ParseProductElement(root, result)) return true;
                    }
                }
                catch
                {
                    // Ignorar JSON malformados
                }
            }

            return false;
        }

        private static string? GetJsonStringValue(JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.String) return elem.GetString();
            if (elem.ValueKind == JsonValueKind.Number) return elem.GetRawText();
            return null;
        }

        private static bool ParseProductElement(JsonElement elem, HeuristicResult result)
        {
            if (elem.TryGetProperty("@type", out var typeProp) && typeProp.GetString() == "Product")
            {
                if (elem.TryGetProperty("name", out var nameProp))
                {
                    result.Nombre = nameProp.GetString();
                }

                if (elem.TryGetProperty("image", out var imageProp))
                {
                    if (imageProp.ValueKind == JsonValueKind.String)
                    {
                        result.ImagenUrl = imageProp.GetString();
                    }
                    else if (imageProp.ValueKind == JsonValueKind.Array && imageProp.GetArrayLength() > 0)
                    {
                        result.ImagenUrl = imageProp[0].GetString();
                    }
                }

                if (elem.TryGetProperty("offers", out var offersProp))
                {
                    if (offersProp.ValueKind == JsonValueKind.Object)
                    {
                        ExtractOfferDetails(offersProp, result);
                    }
                    else if (offersProp.ValueKind == JsonValueKind.Array && offersProp.GetArrayLength() > 0)
                    {
                        ExtractOfferDetails(offersProp[0], result);
                    }
                }

                if (elem.TryGetProperty("sku", out var skuProp))
                {
                    result.Sku = DataNormalizer.NormalizeSku(GetJsonStringValue(skuProp));
                }
                else if (elem.TryGetProperty("gtin", out var gtinProp))
                {
                    result.Sku = DataNormalizer.NormalizeSku(GetJsonStringValue(gtinProp));
                }
                else if (elem.TryGetProperty("mpn", out var mpnProp))
                {
                    result.Sku = DataNormalizer.NormalizeSku(GetJsonStringValue(mpnProp));
                }

                if (elem.TryGetProperty("brand", out var brandProp))
                {
                    if (brandProp.ValueKind == JsonValueKind.String)
                    {
                        result.Marca = DataNormalizer.NormalizeBrand(brandProp.GetString());
                    }
                    else if (brandProp.ValueKind == JsonValueKind.Object)
                    {
                        if (brandProp.TryGetProperty("name", out var brandNameProp))
                        {
                            result.Marca = DataNormalizer.NormalizeBrand(GetJsonStringValue(brandNameProp));
                        }
                    }
                }

                return !string.IsNullOrEmpty(result.Nombre) || result.Precio.HasValue;
            }
            return false;
        }

        private static void ExtractOfferDetails(JsonElement offer, HeuristicResult result)
        {
            if (offer.TryGetProperty("price", out var priceProp))
            {
                string? priceStr = null;
                if (priceProp.ValueKind == JsonValueKind.Number)
                {
                    result.Precio = priceProp.GetDecimal();
                }
                else if (priceProp.ValueKind == JsonValueKind.String)
                {
                    priceStr = priceProp.GetString();
                }

                if (priceStr != null && decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedPrice))
                {
                    result.Precio = parsedPrice;
                }
            }

            if (offer.TryGetProperty("availability", out var availProp))
            {
                var availUrl = availProp.GetString()?.ToLower() ?? "";
                if (availUrl.Contains("outofstock") || availUrl.Contains("soldout") || availUrl.Contains("discontinued"))
                {
                    result.EnStock = false;
                }
            }
        }

        private static bool TryExtractFromMetaTags(HtmlDocument doc, HeuristicResult result)
        {
            var titleNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']") 
                            ?? doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:title']")
                            ?? doc.DocumentNode.SelectSingleNode("//meta[@name='title']");
            if (titleNode != null)
            {
                result.Nombre = titleNode.GetAttributeValue("content", "").Trim();
            }

            var imgNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']") 
                          ?? doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image']")
                          ?? doc.DocumentNode.SelectSingleNode("//meta[@name='image']");
            if (imgNode != null)
            {
                result.ImagenUrl = imgNode.Attributes["content"]?.Value;
            }

            var priceNode = doc.DocumentNode.SelectSingleNode("//meta[@property='product:price:amount']")
                            ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:price:amount']")
                            ?? doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:data1']");
            if (priceNode != null)
            {
                var priceStr = priceNode.GetAttributeValue("content", "").Trim();
                if (decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedPrice))
                {
                    result.Precio = parsedPrice;
                }
            }

            var stockNode = doc.DocumentNode.SelectSingleNode("//meta[@property='product:availability']")
                            ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:availability']");
            if (stockNode != null)
            {
                var stockStr = stockNode.GetAttributeValue("content", "").ToLower();
                if (stockStr.Contains("out of stock") || stockStr.Contains("oos") || stockStr.Contains("instock") == false)
                {
                    if (stockStr.Contains("out") || stockStr.Contains("agotado"))
                    {
                        result.EnStock = false;
                    }
                }
            }

            return !string.IsNullOrEmpty(result.Nombre) && result.Precio.HasValue;
        }

        private static void ExtractFromDomHeuristics(HtmlDocument doc, HeuristicResult result)
        {
            var h1Node = doc.DocumentNode.SelectSingleNode("//h1");
            if (h1Node != null)
            {
                result.Nombre = h1Node.InnerText.Trim();
            }
            else
            {
                var titleNode = doc.DocumentNode.SelectSingleNode("//title");
                if (titleNode != null)
                {
                    var titleText = titleNode.InnerText;
                    var separatorIndex = titleText.IndexOfAny(new[] { '|', '-', '•' });
                    result.Nombre = separatorIndex > 0 ? titleText.Substring(0, separatorIndex).Trim() : titleText.Trim();
                }
            }

            var pageText = doc.DocumentNode.InnerText.ToLower();
            if (pageText.Contains("agotado") || pageText.Contains("sin stock") || pageText.Contains("no disponible") || pageText.Contains("out of stock"))
            {
                result.EnStock = false;
            }

            var mainImg = doc.DocumentNode.SelectSingleNode("//img[contains(@class, 'product') or contains(@id, 'product') or contains(@src, 'product')]")
                          ?? doc.DocumentNode.SelectSingleNode("//img[@id='landingImage' or @id='main-image' or @class='front-image']");
            if (mainImg != null)
            {
                result.ImagenUrl = mainImg.Attributes["src"]?.Value 
                                   ?? mainImg.Attributes["data-src"]?.Value;
            }

            if (h1Node != null)
            {
                var parent = h1Node.ParentNode;
                if (parent != null)
                {
                    var parentText = parent.InnerText;
                    var match = PriceRegex.Match(parentText);
                    if (match.Success)
                    {
                        var priceValStr = match.Groups[1].Value.Replace(",", "");
                        if (decimal.TryParse(priceValStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedPrice))
                        {
                            result.Precio = parsedPrice;
                            // Encontrar el nodo especifico del precio para aprender su selector
                            var priceNode = FindNodeForPrice(doc, parsedPrice);
                            if (priceNode != null)
                            {
                                result.XPathSugerido = GetSmartXPath(priceNode);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Busca en el DOM el nodo de texto mas profundo que contenga el valor numerico del precio.
        /// </summary>
        private static HtmlNode? FindNodeForPrice(HtmlDocument doc, decimal price)
        {
            try
            {
                var priceStr1 = price.ToString("F2"); // ej. "1249.99"
                var priceStr2 = price.ToString("F0"); // ej. "1250"
                var priceStr3 = string.Format("{0:N2}", price); // ej. "1,249.99"
                
                // Buscar nodos de texto que contengan el precio
                var xpathQuery = $"//*[contains(text(), '{priceStr1}') or contains(text(), '{priceStr3}')]";
                var nodes = doc.DocumentNode.SelectNodes(xpathQuery);
                
                if (nodes != null && nodes.Count > 0)
                {
                    HtmlNode? bestNode = null;
                    int minDescendants = int.MaxValue;
                    
                    foreach (var node in nodes)
                    {
                        // Evitar tags que no sean visibles o representen estructura global
                        if (node.Name == "script" || node.Name == "style" || node.Name == "head" || node.Name == "html" || node.Name == "body") continue;
                        
                        var descendantCount = node.SelectNodes(".//*")?.Count ?? 0;
                        if (descendantCount < minDescendants)
                        {
                            minDescendants = descendantCount;
                            bestNode = node;
                        }
                    }
                    return bestNode;
                }
            }
            catch
            {
                // Ignorar fallos de busqueda
            }
            return null;
        }

        /// <summary>
        /// Genera un XPath inteligente y robusto para un nodo, anclándose al ID estable mas cercano en el arbol.
        /// </summary>
        public static string GetSmartXPath(HtmlNode node)
        {
            if (node == null) return string.Empty;

            // 1. Si el propio nodo tiene un ID valido y estable, usarlo directamente
            var id = node.GetAttributeValue("id", string.Empty);
            if (!string.IsNullOrEmpty(id) && !IsDynamicId(id))
            {
                return $"//*[@id='{id}']";
            }

            // 2. Subir por los ancestros buscando un ID estable para anclaje
            var current = node;
            var path = "";
            while (current != null && current.Name != "#document")
            {
                var currentId = current.GetAttributeValue("id", string.Empty);
                if (!string.IsNullOrEmpty(currentId) && !IsDynamicId(currentId))
                {
                    return $"//*[@id='{currentId}']{path}";
                }

                // Determinar indice del nodo entre hermanos del mismo tipo
                var index = 1;
                var sib = current.PreviousSibling;
                while (sib != null)
                {
                    if (sib.Name == current.Name) index++;
                    sib = sib.PreviousSibling;
                }
                
                path = $"/{current.Name}[{index}]" + path;
                current = current.ParentNode;
            }

            return path;
        }

        /// <summary>
        /// Evalua si un ID es dinamico (Angular, Ember, UUIDs, autogenerados) para evitar anclajes fragiles.
        /// </summary>
        private static bool IsDynamicId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return true;
            
            // Filtros de IDs dinamicos comunes
            if (id.Contains("ng-") || id.Contains("mat-") || id.Contains("ember")) return true;
            if (id.Contains("compare-") || id.Contains("quantity_")) return true; // Especificos de Costco/Walmart que varian por SKU
            
            // Validar si parece un UUID/GUID
            if (Regex.IsMatch(id, @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)) return true;
            
            // Validar si parece un ID autogenerado numerico puro muy largo
            if (Regex.IsMatch(id, @"^\d{5,}$")) return true;

            return false;
        }
    }
}
