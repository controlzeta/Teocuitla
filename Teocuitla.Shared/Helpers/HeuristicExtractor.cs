using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text;
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
        public string? FuentePrecio { get; set; }
        public string? PrecioTextoBruto { get; set; }
        public string? Moneda { get; set; }
        public string? XPathPrecio { get; set; }
        public int ConfianzaPrecio { get; set; }
    }

    public static class HeuristicExtractor
    {
        private const int MaxCachedExtractions = 256;
        private static readonly ConcurrentDictionary<string, HeuristicResult> ExtractionCache = new();

        /// <summary>
        /// Extrae heuristicamente los datos de un producto a partir del codigo HTML de la pagina.
        /// </summary>
        public static HeuristicResult Extract(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return new HeuristicResult();

            var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)));
            if (ExtractionCache.TryGetValue(cacheKey, out var cachedResult))
            {
                return CloneResult(cachedResult);
            }

            var result = new HeuristicResult();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var productScope = FindProductScope(doc);

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
                ExtractFromDomHeuristics(doc, productScope, result);
                result.MetodoDeteccion = "Analisis Semantico DOM (Fallback)";
            }

            if (result.Precio.HasValue && FindPriceInDom(productScope, out var currentPriceNode) is decimal contextualPrice &&
                IsCurrentPriceNode(currentPriceNode) && contextualPrice != result.Precio)
            {
                if (SetPrice(result, currentPriceNode!.InnerText, "Precio actual DOM", 80, currentPriceNode))
                {
                    result.XPathSugerido = GetSmartXPath(currentPriceNode);
                    result.MetodoDeteccion += " (Con Corrección de Descuento DOM)";
                }
            }

            // APRENDIZAJE: Si logramos extraer un precio exitosamente pero aun no tenemos un selector (como en JSON-LD o Meta Tags),
            // buscamos en caliente el elemento visual en el DOM que contiene dicho precio y generamos su XPath inteligente.
            if (result.Precio.HasValue && string.IsNullOrEmpty(result.XPathSugerido))
            {
                var priceNode = FindNodeForPrice(productScope, result.Precio.Value);
                if (priceNode != null)
                {
                    result.XPathSugerido = GetSmartXPath(priceNode);
                    result.XPathPrecio = result.XPathSugerido;
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

            CacheResult(cacheKey, result);
            return result;
        }

        private static void CacheResult(string cacheKey, HeuristicResult result)
        {
            if (ExtractionCache.Count >= MaxCachedExtractions)
            {
                var oldestKnownKey = ExtractionCache.Keys.FirstOrDefault();
                if (oldestKnownKey != null)
                {
                    ExtractionCache.TryRemove(oldestKnownKey, out _);
                }
            }

            ExtractionCache.TryAdd(cacheKey, CloneResult(result));
        }

        private static HeuristicResult CloneResult(HeuristicResult source)
        {
            return new HeuristicResult
            {
                Nombre = source.Nombre,
                Precio = source.Precio,
                EnStock = source.EnStock,
                XPathSugerido = source.XPathSugerido,
                ImagenUrl = source.ImagenUrl,
                Sku = source.Sku,
                Marca = source.Marca,
                MetodoDeteccion = source.MetodoDeteccion,
                FuentePrecio = source.FuentePrecio,
                PrecioTextoBruto = source.PrecioTextoBruto,
                Moneda = source.Moneda,
                XPathPrecio = source.XPathPrecio,
                ConfianzaPrecio = source.ConfianzaPrecio
            };
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

                    foreach (var element in EnumerateJsonObjects(root))
                    {
                        var extracted = new HeuristicResult();
                        if (ParseProductElement(element, extracted))
                        {
                            CopyProductResult(extracted, result);
                            return true;
                        }
                    }
                }
                catch
                {
                    // Ignorar JSON malformados
                }
            }

            if (!result.Precio.HasValue || result.Precio.Value <= 0)
            {
                result.Precio = null;
                return false;
            }

            return true;
        }

        private static IEnumerable<JsonElement> EnumerateJsonObjects(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in EnumerateJsonObjects(item)) yield return nested;
                }
                yield break;
            }

            if (element.ValueKind != JsonValueKind.Object) yield break;
            yield return element;

            if (element.TryGetProperty("@graph", out var graph))
            {
                foreach (var nested in EnumerateJsonObjects(graph)) yield return nested;
            }

            if (element.TryGetProperty("hasVariant", out var variants))
            {
                foreach (var nested in EnumerateJsonObjects(variants)) yield return nested;
            }
        }

        private static string? GetJsonStringValue(JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.String) return elem.GetString();
            if (elem.ValueKind == JsonValueKind.Number) return elem.GetRawText();
            return null;
        }

        private static bool ParseProductElement(JsonElement elem, HeuristicResult result)
        {
            if (HasJsonType(elem, "Product"))
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

                if (elem.TryGetProperty("offers", out var offersProp) && TrySelectOffer(offersProp, out var offer))
                {
                    result.Precio = offer.Price;
                    result.PrecioTextoBruto = offer.RawValue;
                    result.Moneda = offer.Currency;
                    result.EnStock = offer.EnStock;
                    result.FuentePrecio = offer.Source;
                    result.ConfianzaPrecio = offer.Confidence;
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

                return result.Precio.HasValue && result.Precio.Value > 0;
            }
            return false;
        }

        private static bool HasJsonType(JsonElement element, string expectedType)
        {
            if (!element.TryGetProperty("@type", out var type)) return false;
            if (type.ValueKind == JsonValueKind.String)
            {
                return string.Equals(type.GetString(), expectedType, StringComparison.OrdinalIgnoreCase);
            }

            return type.ValueKind == JsonValueKind.Array &&
                   type.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String &&
                       string.Equals(item.GetString(), expectedType, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TrySelectOffer(JsonElement offers, out JsonOffer selected)
        {
            var candidates = EnumerateOffers(offers)
                .Select(CreateOffer)
                .Where(candidate => candidate != null)
                .Cast<JsonOffer>()
                .OrderByDescending(candidate => candidate.Rank)
                .ToList();

            selected = candidates.FirstOrDefault()!;
            return selected != null;
        }

        private static IEnumerable<JsonElement> EnumerateOffers(JsonElement offers)
        {
            if (offers.ValueKind == JsonValueKind.Array)
            {
                foreach (var offer in offers.EnumerateArray())
                {
                    foreach (var nested in EnumerateOffers(offer)) yield return nested;
                }
                yield break;
            }

            if (offers.ValueKind != JsonValueKind.Object) yield break;
            if (offers.TryGetProperty("offers", out var nestedOffers))
            {
                foreach (var nested in EnumerateOffers(nestedOffers)) yield return nested;
            }

            if (offers.TryGetProperty("price", out _) || offers.TryGetProperty("lowPrice", out _))
            {
                yield return offers;
            }
        }

        private static JsonOffer? CreateOffer(JsonElement offer)
        {
            var isDirectPrice = offer.TryGetProperty("price", out var priceElement);
            if (!isDirectPrice && !offer.TryGetProperty("lowPrice", out priceElement)) return null;

            var parsed = PriceParser.Parse(GetJsonStringValue(priceElement));
            if (parsed == null) return null;

            var availability = offer.TryGetProperty("availability", out var availabilityElement)
                ? availabilityElement.GetString()?.ToLowerInvariant() ?? string.Empty
                : string.Empty;
            var enStock = !availability.Contains("outofstock") && !availability.Contains("soldout") && !availability.Contains("discontinued");
            var currency = offer.TryGetProperty("priceCurrency", out var currencyElement)
                ? GetJsonStringValue(currencyElement)?.ToUpperInvariant()
                : parsed.Currency;
            var rank = (isDirectPrice ? 100 : 80) + (enStock ? 20 : 0) + (string.IsNullOrEmpty(currency) ? 0 : 5);

            return new JsonOffer(parsed.Price, parsed.RawValue, currency, enStock,
                isDirectPrice ? "JSON-LD" : "JSON-LD (Precio mínimo)", isDirectPrice ? 100 : 90, rank);
        }

        private static void CopyProductResult(HeuristicResult source, HeuristicResult destination)
        {
            destination.Nombre = source.Nombre;
            destination.Precio = source.Precio;
            destination.EnStock = source.EnStock;
            destination.ImagenUrl = source.ImagenUrl;
            destination.Sku = source.Sku;
            destination.Marca = source.Marca;
            destination.FuentePrecio = source.FuentePrecio;
            destination.PrecioTextoBruto = source.PrecioTextoBruto;
            destination.Moneda = source.Moneda;
            destination.ConfianzaPrecio = source.ConfianzaPrecio;
        }

        private sealed record JsonOffer(decimal Price, string RawValue, string? Currency, bool EnStock, string Source, int Confidence, int Rank);

        private static bool TryExtractFromMetaTags(HtmlDocument doc, HeuristicResult result)
        {
            var titleNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']") 
                            ?? doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:title']")
                            ?? doc.DocumentNode.SelectSingleNode("//meta[@name='title']");
            if (titleNode != null && string.IsNullOrEmpty(result.Nombre))
            {
                result.Nombre = titleNode.GetAttributeValue("content", "").Trim();
            }

            var imgNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']") 
                          ?? doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image']")
                          ?? doc.DocumentNode.SelectSingleNode("//meta[@name='image']");
            if (imgNode != null && string.IsNullOrEmpty(result.ImagenUrl))
            {
                result.ImagenUrl = imgNode.Attributes["content"]?.Value;
            }

            var isAmazon = doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:site']")?.GetAttributeValue("content", "")?.Contains("Amazon", StringComparison.OrdinalIgnoreCase) == true
                           || doc.DocumentNode.SelectSingleNode("//title")?.InnerText.Contains("Amazon", StringComparison.OrdinalIgnoreCase) == true;

            var priceNode = doc.DocumentNode.SelectSingleNode("//meta[@property='product:price:amount']")
                            ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:price:amount']")
                            ?? (isAmazon ? null : doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:data1']"));
            if (priceNode != null && (!result.Precio.HasValue || result.Precio <= 0))
            {
                var priceStr = priceNode.GetAttributeValue("content", "").Trim();
                SetPrice(result, priceStr, "Meta etiqueta", 90);
            }

            var stockNode = doc.DocumentNode.SelectSingleNode("//meta[@property='product:availability']")
                            ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:availability']");
            if (stockNode != null)
            {
                var stockStr = stockNode.GetAttributeValue("content", "").ToLower();
                if (stockStr.Contains("instock"))
                {
                    result.EnStock = true;
                }
                else if (stockStr.Contains("out of stock") || stockStr.Contains("oos") || stockStr.Contains("out") || stockStr.Contains("agotado"))
                {
                    result.EnStock = false;
                }
            }

            return !string.IsNullOrEmpty(result.Nombre) && (!result.EnStock || (result.Precio.HasValue && result.Precio.Value > 0));
        }

        private static void ExtractFromDomHeuristics(HtmlDocument doc, HtmlNode productScope, HeuristicResult result)
        {
            var h1Node = productScope.SelectSingleNode(".//h1") ?? doc.DocumentNode.SelectSingleNode("//h1");
            if (string.IsNullOrEmpty(result.Nombre))
            {
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
            }

            var availNode = productScope.SelectSingleNode(".//*[@id='availability']")
                            ?? productScope.SelectSingleNode(".//*[contains(@id, 'availability') or contains(@class, 'availability') or contains(@id, 'stock') or contains(@class, 'stock') or contains(@class, 'no_stock') or contains(@class, 'ui-pdp-message')]");
            if (availNode != null)
            {
                var availText = availNode.InnerText.ToLower();
                if (availText.Contains("no disponible") || availText.Contains("no está disponible") || availText.Contains("out of stock") || availText.Contains("agotado") || availText.Contains("sin stock") || availText.Contains("pausada"))
                {
                    result.EnStock = false;
                }
                else if (availText.Contains("en stock") || availText.Contains("disponible") || availText.Contains("in stock") || availText.Contains("sólo queda") || availText.Contains("solo queda") || availText.Contains("disponibles"))
                {
                    result.EnStock = true;
                }
            }

            var pageText = productScope.InnerText.ToLower();
            if (pageText.Contains("este producto no está disponible") || pageText.Contains("producto no disponible") || pageText.Contains("publicación pausada") || pageText.Contains("currently unavailable"))
            {
                result.EnStock = false;
            }

            var mainImg = productScope.SelectSingleNode(".//img[contains(@class, 'product') or contains(@id, 'product') or contains(@src, 'product')]")
                          ?? productScope.SelectSingleNode(".//img[@id='landingImage' or @id='main-image' or @class='front-image']");
            if (mainImg != null && string.IsNullOrEmpty(result.ImagenUrl))
            {
                result.ImagenUrl = mainImg.Attributes["src"]?.Value 
                                   ?? mainImg.Attributes["data-src"]?.Value;
            }

            if (!result.Precio.HasValue)
            {
                var priceVal = FindPriceInDom(productScope, out var priceNode);
                if (priceVal.HasValue)
                {
                    result.Precio = priceVal.Value;
                    if (priceNode != null)
                    {
                        result.XPathSugerido = GetSmartXPath(priceNode);
                        result.XPathPrecio = result.XPathSugerido;
                        SetPrice(result, priceNode.InnerText, "DOM semántico", 50, priceNode);
                    }
                }
            }
        }

        private static bool SetPrice(HeuristicResult result, string? rawPrice, string source, int confidence, HtmlNode? priceNode = null)
        {
            var parsed = PriceParser.Parse(rawPrice);
            if (parsed == null) return false;

            result.Precio = parsed.Price;
            result.PrecioTextoBruto = parsed.RawValue;
            result.Moneda = parsed.Currency ?? result.Moneda;
            result.FuentePrecio = source;
            result.ConfianzaPrecio = confidence;
            if (priceNode != null)
            {
                result.XPathPrecio = GetSmartXPath(priceNode);
            }

            return true;
        }

        private static HtmlNode FindProductScope(HtmlDocument doc)
        {
            var h1 = doc.DocumentNode.SelectSingleNode("//h1");
            for (var current = h1; current != null && current.Name != "#document"; current = current.ParentNode)
            {
                var attributes = $"{current.GetAttributeValue("id", string.Empty)} {current.GetAttributeValue("class", string.Empty)} {current.GetAttributeValue("itemtype", string.Empty)}";
                if (current.Name.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                    attributes.Contains("product", StringComparison.OrdinalIgnoreCase) ||
                    attributes.Contains("pdp", StringComparison.OrdinalIgnoreCase))
                {
                    return current;
                }
            }

            return doc.DocumentNode.SelectSingleNode("//main") ?? doc.DocumentNode;
        }

        private static bool ShouldExcludeNode(HtmlNode node)
        {
            var current = node;
            int depth = 0;
            while (current != null && depth < 100)
            {
                var id = current.GetAttributeValue("id", "").ToLower();
                var @class = current.GetAttributeValue("class", "").ToLower();

                // Excluir planes de garantía, protección o seguros
                if (id.Contains("warranty") || id.Contains("protection") || id.Contains("seguro") || id.Contains("insurance") || id.Contains("coaseguro")
                    || @class.Contains("warranty") || @class.Contains("protection") || @class.Contains("seguro") || @class.Contains("insurance"))
                {
                    return true;
                }

                // Excluir carruseles, recomendaciones, patrocinados, anuncios, productos relacionados y alternativas fuera de stock
                if (id.Contains("carousel") || id.Contains("recommend") || id.Contains("similar") || id.Contains("related") || id.Contains("sponsored")
                    || id.Contains("upsell") || id.Contains("fbt") || id.Contains("bought") || id.Contains("viewed") || id.Contains("purchased")
                    || id.Contains("personalization") || id.Contains("alternative") || id.Contains("recs") || id.Contains("ad-") || id.Contains("widget") || id.Contains("oos")
                    || id.Contains("cardinstance") || id.Contains("celwidget")
                    || @class.Contains("carousel") || @class.Contains("recommend") || @class.Contains("similar") || @class.Contains("related") || @class.Contains("sponsored")
                    || @class.Contains("upsell") || @class.Contains("fbt") || @class.Contains("bought") || @class.Contains("viewed") || @class.Contains("purchased")
                    || @class.Contains("personalization") || @class.Contains("alternative") || @class.Contains("recs") || @class.Contains("ad-") || @class.Contains("widget") || @class.Contains("oos")
                    || @class.Contains("cardinstance") || @class.Contains("celwidget"))
                {
                    return true;
                }

                current = current.ParentNode;
                depth++;
            }
            return false;
        }

        private static decimal? FindPriceInDom(HtmlNode scope, out HtmlNode? priceNode)
        {
            priceNode = null;
            var priceCandidates = scope.SelectNodes(".//*[@itemprop='price' or contains(@class, 'price') or contains(@class, 'amount') or contains(@id, 'price') or contains(@class, 'special') or contains(@class, 'promo') or contains(@data-testid, 'price')]");
            if (priceCandidates != null)
            {
                var bestCandidate = priceCandidates
                    .Where(candidate => candidate.InnerText.Length <= 100 && !ShouldExcludeNode(candidate))
                    .Select(candidate => new DomPriceCandidate(candidate, PriceParser.Parse(candidate.InnerText)))
                    .Where(candidate => candidate.ParsedPrice != null)
                    .OrderByDescending(ScorePriceCandidate)
                    .FirstOrDefault();

                if (bestCandidate != null)
                {
                    priceNode = bestCandidate.Node;
                    return bestCandidate.ParsedPrice!.Price;
                }
            }

            var allTextNodes = scope.SelectNodes(".//text()");
            if (allTextNodes != null)
            {
                foreach (var node in allTextNodes)
                {
                    var txt = node.InnerText.Trim();
                    if (txt.Length < 30 && (txt.Contains("$") || Regex.IsMatch(txt, @"\b(?:MXN|USD|EUR|GBP)\b", RegexOptions.IgnoreCase)))
                    {
                        if (node.ParentNode != null && ShouldExcludeNode(node.ParentNode)) continue;
                        var parsedPrice = PriceParser.Parse(txt);
                        if (parsedPrice != null)
                        {
                            if (parsedPrice.Price > 0)
                            {
                                priceNode = node.ParentNode;
                                return parsedPrice.Price;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private static int ScorePriceCandidate(DomPriceCandidate candidate)
        {
            var attributes = $"{candidate.Node.GetAttributeValue("id", string.Empty)} {candidate.Node.GetAttributeValue("class", string.Empty)} {candidate.Node.GetAttributeValue("data-testid", string.Empty)}".ToLowerInvariant();
            var score = 10;
            if (candidate.Node.GetAttributeValue("itemprop", string.Empty).Equals("price", StringComparison.OrdinalIgnoreCase)) score += 60;
            if (attributes.Contains("current") || attributes.Contains("final") || attributes.Contains("sale") || attributes.Contains("discount") || attributes.Contains("special")) score += 40;
            if (attributes.Contains("price") || attributes.Contains("amount")) score += 20;
            if (attributes.Contains("old") || attributes.Contains("original") || attributes.Contains("list-price") || attributes.Contains("was")) score -= 100;
            if (attributes.Contains("monthly") || attributes.Contains("mensual") || attributes.Contains("msi") || attributes.Contains("installment")) score -= 100;
            return score;
        }

        private static bool IsCurrentPriceNode(HtmlNode? node)
        {
            if (node == null) return false;
            var attributes = $"{node.GetAttributeValue("id", string.Empty)} {node.GetAttributeValue("class", string.Empty)}".ToLowerInvariant();
            return attributes.Contains("current") || attributes.Contains("final") || attributes.Contains("sale") ||
                   attributes.Contains("discount") || attributes.Contains("special");
        }

        private sealed record DomPriceCandidate(HtmlNode Node, PriceParseResult? ParsedPrice);

        /// <summary>
        /// Busca en el DOM el nodo de texto mas profundo que contenga el valor numerico del precio.
        /// </summary>
        private static HtmlNode? FindNodeForPrice(HtmlNode scope, decimal price)
        {
            try
            {
                var priceStr1 = price.ToString("F2"); // ej. "1249.99"
                var priceStr2 = price.ToString("F0"); // ej. "1250"
                var priceStr3 = string.Format("{0:N2}", price); // ej. "1,249.99"
                
                // Buscar nodos de texto que contengan el precio
                var xpathQuery = $"//*[contains(text(), '{priceStr1}') or contains(text(), '{priceStr3}')]";
                var nodes = scope.SelectNodes($".{xpathQuery}");
                
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
