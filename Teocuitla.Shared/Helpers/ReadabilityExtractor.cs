using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Teocuitla.Shared.Helpers
{
    public static class ReadabilityExtractor
    {
        private static readonly Regex RegexUnlikelyCandidates = new Regex(
            @"ad-break|ad-container|ad-wrapper|adsense|advertisement|announcement|author|bio|bookmark|bottom|breadcrumbs|combobox|comment|contact|credits|disqus|extra|foot|footer|footnote|header|header-menu|headline-link|login|masthead|media-credit|menu|meta|nav|navigation|outbrain|pager|pagination|popup|related|reply|robots|rss|shiba|sidebar|site-index|social|sponsor|subscription|tab|teaser|toolbar|trackback|widget|zone",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RegexOkMaybeItsACandidate = new Regex(
            @"and|article|body|column|content|entry|main|page|pagination|post|text|story",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RegexPositive = new Regex(
            @"article|body|content|entry|main|page|post|text",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RegexNegative = new Regex(
            @"comment|combobox|contact|foot|footer|header|menu|nav|sidebar|sponsor|social|widget|ad",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] TagsToRemove = new[]
        {
            "script", "style", "noscript", "iframe", "head", "link", "meta",
            "header", "footer", "nav", "aside", "form", "svg", "canvas",
            "object", "embed"
        };

        /// <summary>
        /// Realiza la limpieza previa del documento eliminando scripts, estilos, comentarios,
        /// elementos ocultos y nodos improbables.
        /// </summary>
        public static void PreClean(HtmlDocument doc)
        {
            if (doc == null || doc.DocumentNode == null) return;

            // 1. Eliminar comentarios HTML
            var comments = doc.DocumentNode.Descendants()
                .Where(n => n.NodeType == HtmlNodeType.Comment)
                .ToList();
            foreach (var comment in comments)
            {
                try { comment.Remove(); } catch { }
            }

            // 2. Eliminar etiquetas no deseadas
            var badNodes = doc.DocumentNode.Descendants()
                .Where(n => TagsToRemove.Contains(n.Name.ToLower()))
                .ToList();
            foreach (var node in badNodes)
            {
                try { node.Remove(); } catch { }
            }

            // 3. Eliminar elementos con estilos de ocultamiento
            var hiddenNodes = doc.DocumentNode.Descendants()
                .Where(n =>
                {
                    var style = n.GetAttributeValue("style", "").ToLower();
                    return style.Contains("display:none") || style.Contains("display: none") ||
                           style.Contains("visibility:hidden") || style.Contains("visibility: hidden") ||
                           style.Contains("opacity:0") || style.Contains("opacity: 0");
                })
                .ToList();
            foreach (var node in hiddenNodes)
            {
                try { node.Remove(); } catch { }
            }

            // 4. Eliminar candidatos improbables por clase/ID
            var allNodes = doc.DocumentNode.Descendants().ToList();
            foreach (var node in allNodes)
            {
                if (node.Name == "body" || node.Name == "html" || node.ParentNode == null) continue;

                var className = node.GetAttributeValue("class", "");
                var idName = node.GetAttributeValue("id", "");
                var combined = $"{className} {idName}";

                if (RegexUnlikelyCandidates.IsMatch(combined) && !RegexOkMaybeItsACandidate.IsMatch(combined))
                {
                    try { node.Remove(); } catch { }
                }
            }
        }

        /// <summary>
        /// Puntuación de nodos basada en heurísticas de densidad de texto y enlaces.
        /// </summary>
        public static Dictionary<HtmlNode, double> ScoreNodes(HtmlDocument doc)
        {
            var nodeScores = new Dictionary<HtmlNode, double>();
            if (doc == null || doc.DocumentNode == null) return nodeScores;

            // Nodos candidatos para contener texto
            var candidateTags = new[] { "p", "div", "td", "span", "section", "article" };
            var candidates = doc.DocumentNode.Descendants()
                .Where(n => candidateTags.Contains(n.Name.ToLower()))
                .ToList();

            foreach (var candidate in candidates)
            {
                var text = candidate.InnerText;
                if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 25) continue;

                // Inicializar puntuación
                double score = 0;

                // Sumar/restar por clases o IDs positivos y negativos
                var className = candidate.GetAttributeValue("class", "");
                var idName = candidate.GetAttributeValue("id", "");
                var combined = $"{className} {idName}";

                if (RegexPositive.IsMatch(combined)) score += 25;
                if (RegexNegative.IsMatch(combined)) score -= 25;

                // Densidad de texto y puntuación estructurada
                score += 1; // 1 punto por el nodo en sí
                score += text.Length / 100.0; // 1 punto por cada 100 caracteres

                // Puntuación por comas y signos de puntuación (estructura de oraciones)
                int punctuationCount = text.Count(c => c == ',' || c == ';' || c == '.' || c == '?' || c == '!' || c == '-');
                score += punctuationCount;

                // Penalización por densidad de enlaces (Link Density)
                double linkDensity = CalculateLinkDensity(candidate);
                score *= (1.0 - linkDensity);

                // Agregar puntuación acumulada al propio nodo
                if (!nodeScores.ContainsKey(candidate))
                {
                    nodeScores[candidate] = 0;
                }
                nodeScores[candidate] += score;

                // Propagar puntuación a padres y abuelos
                var parent = candidate.ParentNode;
                if (parent != null && parent.Name != "#document" && parent.Name != "html")
                {
                    if (!nodeScores.ContainsKey(parent))
                    {
                        nodeScores[parent] = 0;
                    }
                    nodeScores[parent] += score; // 100% al padre

                    var grandparent = parent.ParentNode;
                    if (grandparent != null && grandparent.Name != "#document" && grandparent.Name != "html")
                    {
                        if (!nodeScores.ContainsKey(grandparent))
                        {
                            nodeScores[grandparent] = 0;
                        }
                        nodeScores[grandparent] += score / 2.0; // 50% al abuelo
                    }
                }
            }

            // Aplicar la penalización final por densidad de enlaces a los contenedores acumulados
            var scoredNodesList = nodeScores.Keys.ToList();
            foreach (var node in scoredNodesList)
            {
                double linkDensity = CalculateLinkDensity(node);
                nodeScores[node] *= (1.0 - linkDensity);
            }

            return nodeScores;
        }

        private static double CalculateLinkDensity(HtmlNode node)
        {
            var textLength = node.InnerText?.Length ?? 0;
            if (textLength == 0) return 0;

            var links = node.SelectNodes(".//a");
            if (links == null) return 0;

            var linkTextLength = links.Sum(l => l.InnerText?.Length ?? 0);
            return (double)linkTextLength / textLength;
        }

        /// <summary>
        /// Extrae el nodo principal que contiene la información más legible de la página.
        /// </summary>
        public static HtmlNode? ExtractMainContentNode(HtmlDocument doc)
        {
            PreClean(doc);
            var scores = ScoreNodes(doc);

            if (scores.Count == 0) return null;

            // Ordenar de mayor a menor puntuación y retornar el primero
            return scores.OrderByDescending(kvp => kvp.Value).First().Key;
        }

        /// <summary>
        /// Extrae el texto legible principal de un HTML.
        /// </summary>
        public static string ExtractMainContentText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var bestNode = ExtractMainContentNode(doc);
            if (bestNode == null) return string.Empty;

            return HtmlEntity.DeEntitize(bestNode.InnerText).Trim();
        }

        /// <summary>
        /// Extrae el HTML limpio del contenido principal de una página.
        /// </summary>
        public static string ExtractMainContentHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var bestNode = ExtractMainContentNode(doc);
            return bestNode?.WriteTo() ?? string.Empty;
        }
    }
}
