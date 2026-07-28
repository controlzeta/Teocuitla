using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Teocuitla.Shared.Helpers
{
    public static class RepetitivePatternDetector
    {
        private static readonly Regex RegexPriceText = new Regex(@"\$\s*\d+(?:[.,]\d+)?", RegexOptions.Compiled);
        private static readonly Regex RegexDynamicClass = new Regex(@"\d{4,}|ng-|mat-|ember|^[0-9a-f]{8}-[0-9a-f]{4}-", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RegexLayoutUtilityClass = new Regex(
            @"^(?:col-|row-|flex-|grid-|w-|h-|order-|justify-|align-|self-|items-|content-|text-|bg-|border-|rounded-|shadow-|opacity-|z-|transition-|duration-|delay-|ease-|overflow-|cursor-|pointer-events-|select-|resize-|border-style-|border-width-|border-color-|divide-|ring-|outline-)|" +
            @"^(?:row|flex|grid|container|ng-star-inserted|bootstrap)$|" +
            @"^[pmgtbxy]-[0-9]+$|" +
            @"^(?:sm|md|lg|xl|2xl|focus|hover|active|disabled|visited|dark|motion-reduce|motion-safe|first|last|odd|even):",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public class SiblingGroup
        {
            public HtmlNode Parent { get; set; } = null!;
            public string TagName { get; set; } = string.Empty;
            public string NormalizedClass { get; set; } = string.Empty;
            public List<HtmlNode> Siblings { get; set; } = new();
            public double Score { get; set; }
        }

        /// <summary>
        /// Busca patrones de elementos hermanos repetidos en el DOM para deducir la estructura del catálogo o lista.
        /// </summary>
        public static CategoryScraper.HeuristicSelectors? DetectSelectors(HtmlDocument doc, string baseUrl)
        {
            if (doc == null || doc.DocumentNode == null) return null;

            var allNodes = doc.DocumentNode.Descendants().ToList();
            var groups = new List<SiblingGroup>();

            foreach (var node in allNodes)
            {
                var children = node.ChildNodes
                    .Where(c => c.NodeType == HtmlNodeType.Element && !EsEtiquetaRuido(c.Name))
                    .ToList();

                if (children.Count < 3) continue;

                // Agrupar hijos por su tag name y clase normalizada
                var groupedChildren = children
                    .GroupBy(c => new { Tag = c.Name.ToLower(), Class = NormalizeClasses(c.GetAttributeValue("class", "")) })
                    .Where(g => g.Count() >= 3)
                    .ToList();

                foreach (var g in groupedChildren)
                {
                    var siblingsList = g.ToList();
                    double groupScore = CalculateGroupScore(siblingsList);

                    groups.Add(new SiblingGroup
                    {
                        Parent = node,
                        TagName = g.Key.Tag,
                        NormalizedClass = g.Key.Class,
                        Siblings = siblingsList,
                        Score = groupScore
                    });
                }
            }

            if (groups.Count == 0) return null;

            // Ordenar por puntuación descendente
            var bestGroup = groups.OrderByDescending(g => g.Score).FirstOrDefault();
            if (bestGroup == null || bestGroup.Score < 10) return null; // Umbral mínimo de calidad estructural

            // Generar selectores sugeridos a partir del mejor grupo de hermanos
            return BuildSelectorsForGroup(bestGroup, baseUrl);
        }

        private static bool EsEtiquetaRuido(string tagName)
        {
            var name = tagName.ToLower();
            return name == "script" || name == "style" || name == "noscript" || 
                   name == "br" || name == "hr" || name == "iframe" || 
                   name == "head" || name == "link" || name == "meta";
        }

        private static string NormalizeClasses(string classAttr)
        {
            if (string.IsNullOrWhiteSpace(classAttr)) return string.Empty;

            var parts = classAttr.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c) && !RegexDynamicClass.IsMatch(c) && !RegexLayoutUtilityClass.IsMatch(c))
                .OrderBy(c => c)
                .ToList();

            return string.Join(".", parts);
        }

        private static double CalculateGroupScore(List<HtmlNode> nodes)
        {
            double totalScore = 0;

            foreach (var node in nodes)
            {
                double nodeScore = 0;
                var descendants = node.Descendants().ToList();

                // 1. Enlace
                var hasLink = node.Name == "a" || descendants.Any(d => d.Name == "a" && d.Attributes.Contains("href"));
                if (hasLink) nodeScore += 10;

                // 2. Imagen
                var hasImg = node.Name == "img" || descendants.Any(d => d.Name == "img" || d.Name == "picture");
                if (hasImg) nodeScore += 5;

                // 3. Precio
                var hasPrice = descendants.Any(d => d.Name == "#text" && RegexPriceText.IsMatch(d.InnerText));
                if (hasPrice) nodeScore += 10;

                // 4. Nombre
                var hasName = descendants.Any(d => d.Name == "h1" || d.Name == "h2" || d.Name == "h3" || d.Name == "h4" ||
                                                   (d.GetAttributeValue("class", "") is string cls && (cls.Contains("name") || cls.Contains("title") || cls.Contains("titulo"))));
                if (hasName) nodeScore += 5;

                totalScore += nodeScore;
            }

            // Puntuación promedio por elemento multiplicada por un factor de volumen (para preferir listas más completas)
            double averageScore = totalScore / nodes.Count;
            return averageScore * Math.Log(nodes.Count + 1);
        }

        private static CategoryScraper.HeuristicSelectors BuildSelectorsForGroup(SiblingGroup group, string baseUrl)
        {
            // 1. Obtener la ruta del contenedor (padre)
            string parentXPath = GetSmartXPathForParent(group.Parent);

            // 2. Deducir la ruta de los items en base al tag y clases normalizadas
            string itemPredicate = "";
            if (!string.IsNullOrEmpty(group.NormalizedClass))
            {
                var classParts = group.NormalizedClass.Split('.');
                // Tomar el primer class name significativo para el XPath para evitar rigidez
                var primaryClass = classParts.First();
                itemPredicate = $"[contains(@class, '{primaryClass}')]";
            }
            string containerXPath = $"{parentXPath}/{group.TagName}{itemPredicate}";

            // Asegurar que el contenedor requiera enlaces e imágenes en su evaluación visual
            containerXPath += "[(self::a or .//a) and .//img]";

            // 3. Generar selectores relativos robustos
            return new CategoryScraper.HeuristicSelectors
            {
                Container = containerXPath,
                Nombre = ".//h3[1] | .//h2[1] | .//h4[1] | .//h5[1] | .//*[contains(@class, 'name') or contains(@class, 'title') or contains(@class, 'titulo')][not(self::a) or normalize-space(.)][1] | .//a[normalize-space(.) and @href][1] | .//a[normalize-space(.)][1]",
                Precio = ".//*[contains(@class, 'discount') or contains(@class, 'promo') or contains(@class, 'sale') or contains(@class, 'special') or contains(@class, 'current') or contains(@class, 'price') or contains(@class, 'precio') or contains(@class, 'amount') or contains(@class, 'money')][contains(text(), '$') or .//*[contains(text(), '$')]][1] | .//*[contains(text(), '$')][1] | .//*[contains(@class, 'price') or contains(@class, 'precio')][1]",
                Link = ".//a[contains(@class, 'product') or contains(@class, 'item') or contains(@class, 'name') or contains(@class, 'title') or contains(@class, 'link')][normalize-space(.)][1] | .//a[@href and normalize-space(.)][1] | .//a[@href][1]",
                Imagen = ".//img[contains(@class, 'product') or contains(@class, 'item') or contains(@class, 'main') or contains(@class, 'primary') or contains(@class, 'image') or contains(@class, 'img')][1] | .//img[1] | .//*[contains(@style, 'background-image')][1]",
                Paginador = "//a[@rel='next'][1] | //a[contains(@class, 'next') or contains(@class, 'siguiente') or contains(@aria-label, 'siguiente') or contains(@title, 'siguiente') or contains(text(), 'Siguiente') or contains(text(), 'Next')][1]"
            };
        }

        private static string GetSmartXPathForParent(HtmlNode node)
        {
            if (node == null) return string.Empty;

            var id = node.GetAttributeValue("id", "").Trim();
            if (!string.IsNullOrEmpty(id) && !RegexDynamicClass.IsMatch(id))
            {
                return $"//*[@id='{id}']";
            }

            var pathElements = new List<string>();
            var current = node;

            while (current != null && current.Name != "#document" && current.Name != "html")
            {
                var curId = current.GetAttributeValue("id", "").Trim();
                if (!string.IsNullOrEmpty(curId) && !RegexDynamicClass.IsMatch(curId))
                {
                    pathElements.Insert(0, $"*[@id='{curId}']");
                    break;
                }

                // Obtener el índice entre hermanos del mismo tag
                int index = 1;
                var sibling = current.PreviousSibling;
                while (sibling != null)
                {
                    if (sibling.Name == current.Name) index++;
                    sibling = sibling.PreviousSibling;
                }

                pathElements.Insert(0, $"{current.Name}[{index}]");
                current = current.ParentNode;
            }

            return "//" + string.Join("/", pathElements);
        }
    }
}
