using System;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Teocuitla.Shared.Helpers
{
    public static class DataNormalizer
    {
        private static readonly Regex PriceCleanRegex = new Regex(@"[^\d.,]", RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex TrailingGarbageRegex = new Regex(@"\s*[-|•/]+(?:\s+[-|•/]+)*\s*$", RegexOptions.Compiled);

        private static readonly string[] NoisePatterns = new[]
        {
            @"\b\d+\s*%\s*OFF\b",
            @"\b(?:envío|envio)\s+gratis\b",
            @"\bfree\s+shipping\b",
            @"\b\d+\s*(?:msi|meses\s+sin\s+intereses)\b",
            @"\bllega\s+mañana\b",
            @"\b(?:oferta|descuento|ahorro|promoción|promocion|especial|precio)\b"
        };

        public static string NormalizeName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return string.Empty;

            var text = HtmlEntity.DeEntitize(rawName).Trim();
            text = MultiSpaceRegex.Replace(text, " ");

            foreach (var pattern in NoisePatterns)
            {
                text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase).Trim();
            }

            // Remove empty parentheses/brackets/braces
            text = Regex.Replace(text, @"\(\s*\)|\[\s*\]|\{\s*\}", "");

            text = TrailingGarbageRegex.Replace(text, "");
            text = MultiSpaceRegex.Replace(text, " ").Trim();

            return text;
        }

        public static string NormalizeNameNode(HtmlNode nombreNode)
        {
            if (nombreNode == null) return string.Empty;
            
            var clone = nombreNode.CloneNode(true);
            var ruidoNodes = clone.SelectNodes(".//*[contains(@class, 'promo') or contains(@class, 'discount') or contains(@class, 'badge') or contains(@class, 'tag') or contains(@class, 'offer') or contains(@class, 'sale') or contains(@class, 'shipping') or contains(@class, 'envio') or contains(@class, 'ahorro') or contains(@class, 'flag') or contains(text(), '%') or contains(text(), 'OFF')]");
            if (ruidoNodes != null)
            {
                foreach (var rNode in ruidoNodes)
                {
                    try { rNode.Remove(); } catch { }
                }
            }
            return NormalizeName(clone.InnerText);
        }

        public static decimal? NormalizePrice(string? rawPrice)
        {
            if (string.IsNullOrWhiteSpace(rawPrice)) return null;

            try
            {
                var clean = PriceCleanRegex.Replace(rawPrice, "").Trim();
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

        public static decimal? NormalizePriceNode(HtmlNode priceNode)
        {
            if (priceNode == null) return null;

            var cleanPriceNode = priceNode.CloneNode(true);
            var struckNodes = cleanPriceNode.SelectNodes(".//*[contains(@style, 'line-through') or name()='del' or name()='s' or contains(@class, 'old') or contains(@class, 'original') or contains(@class, 'list-price')]");
            if (struckNodes != null)
            {
                foreach (var sNode in struckNodes)
                {
                    try { sNode.Remove(); } catch { }
                }
            }
            var promoNoiseNodes = cleanPriceNode.SelectNodes(".//*[contains(@class, 'promo') or contains(@class, 'discount') or contains(@class, 'badge') or contains(@class, 'tag') or contains(@class, 'offer') or contains(@class, 'sale') or contains(@class, 'shipping') or contains(@class, 'envio') or contains(@class, 'ahorro') or contains(@class, 'flag') or contains(text(), '%') or contains(text(), 'OFF')]");
            if (promoNoiseNodes != null)
            {
                foreach (var pNode in promoNoiseNodes)
                {
                    try { pNode.Remove(); } catch { }
                }
            }

            var wholeNode = cleanPriceNode.SelectSingleNode(".//*[contains(@class, 'fraction') or contains(@class, 'whole') or contains(@class, 'integer') or contains(@class, 'price-whole') or contains(@class, 'main-price') or contains(@class, 'amount') or contains(@class, 'price-amount')]");
            var centsNode = cleanPriceNode.SelectSingleNode(".//*[contains(@class, 'cents') or contains(@class, 'decimal') or contains(@class, 'fraction-cents') or contains(@class, 'price-cents') or contains(@class, 'centavos')]");

            if (wholeNode == null || centsNode == null)
            {
                var leafDigitNodes = cleanPriceNode.Descendants()
                    .Where(n => !n.HasChildNodes && Regex.IsMatch(n.InnerText, @"\d+"))
                    .ToList();

                if (leafDigitNodes.Count >= 2)
                {
                    var firstDigits = Regex.Replace(leafDigitNodes[0].InnerText.Trim(), @"\D", "");
                    var secondDigits = Regex.Replace(leafDigitNodes[1].InnerText.Trim(), @"\D", "");

                    if (!string.IsNullOrEmpty(firstDigits) && !string.IsNullOrEmpty(secondDigits) && secondDigits.Length <= 2)
                    {
                        wholeNode = leafDigitNodes[0];
                        centsNode = leafDigitNodes[1];
                    }
                }
            }

            if (wholeNode != null && centsNode != null && wholeNode.XPath == centsNode.XPath)
            {
                centsNode = null;
            }

            if (wholeNode != null)
            {
                var wholeText = Regex.Replace(wholeNode.InnerText.Trim(), @"[^\d.,]", "");
                var centsText = centsNode != null ? Regex.Replace(centsNode.InnerText.Trim(), @"\D", "") : "00";
                
                if (centsText.Length == 1) centsText += "0";
                if (centsText.Length > 2) centsText = centsText.Substring(0, 2);
                if (string.IsNullOrEmpty(centsText)) centsText = "00";

                var combined = $"{wholeText}.{centsText}";
                return NormalizePrice(combined);
            }

            return NormalizePrice(cleanPriceNode.InnerText);
        }

        public static bool NormalizeStock(string? rawStock, string? pageText)
        {
            if (!string.IsNullOrWhiteSpace(rawStock))
            {
                var stockLower = rawStock.ToLower();
                if (stockLower.Contains("out of stock") || stockLower.Contains("oos") || stockLower.Contains("agotado") || stockLower.Contains("sin stock") || stockLower.Contains("no disponible") || stockLower.Contains("soldout") || stockLower.Contains("discontinued"))
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(pageText))
            {
                var textLower = pageText.ToLower();
                if (textLower.Contains("agotado") || textLower.Contains("sin stock") || textLower.Contains("no disponible") || textLower.Contains("out of stock") || textLower.Contains("sold out"))
                {
                    return false;
                }
            }

            return true;
        }

        public static string MakeAbsoluteUrl(string? url, string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            url = url.Trim();

            if (url.StartsWith("//"))
            {
                return "https:" + url;
            }

            if (url.StartsWith("/"))
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) return url;
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

        public static string NormalizeBrand(string? rawBrand)
        {
            if (string.IsNullOrWhiteSpace(rawBrand)) return "Genérica";

            var brand = HtmlEntity.DeEntitize(rawBrand).Trim();
            
            // Si contiene barras diagonales, quedarse con la primera parte (ej. VIDANAT/VITAMINAS -> VIDANAT)
            if (brand.Contains("/"))
            {
                brand = brand.Split('/')[0];
            }
            
            brand = MultiSpaceRegex.Replace(brand, " ").Trim();
            
            if (string.IsNullOrEmpty(brand) || brand.Length < 2)
            {
                return "Genérica";
            }

            return brand;
        }

        public static string NormalizeSku(string? rawSku)
        {
            if (string.IsNullOrWhiteSpace(rawSku)) return string.Empty;
            
            var sku = HtmlEntity.DeEntitize(rawSku).Trim();
            
            // Quitar prefijos comunes e indicativos
            sku = Regex.Replace(sku, @"^(?:sku|código|codigo|ref|model|modelo|sku\s*:)\s*", "", RegexOptions.IgnoreCase);
            
            return sku.Trim();
        }

        public static string NormalizeImageUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            
            url = url.Trim();
            
            // Si es un CDN de Shopify, estandarizar a alta resolución (width=2000)
            if (url.Contains("/cdn/shop/") || url.Contains("cdn.shopify.com"))
            {
                if (url.Contains("width="))
                {
                    url = Regex.Replace(url, @"([?&]width=)\d+", "${1}2000");
                }
                else if (url.Contains("&amp;width="))
                {
                    url = Regex.Replace(url, @"(&amp;width=)\d+", "${1}2000");
                }
                else
                {
                    if (url.Contains("?"))
                    {
                        url += "&width=2000";
                    }
                    else
                    {
                        url += "?width=2000";
                    }
                }
            }
            
            return url;
        }
    }
}
