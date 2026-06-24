using System;

namespace Teocuitla.Shared.Helpers
{
    public static class AntibotDetector
    {
        public static string DetectBestStrategy(string html, string? errorMessage, string? title)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                // Si no hay HTML pero hay un mensaje de error o timeout, evaluar si indica bloqueo
                if (errorMessage != null && (
                    errorMessage.Contains("403") || 
                    errorMessage.Contains("Forbidden") || 
                    errorMessage.Contains("Unauthorized") ||
                    errorMessage.Contains("Access Denied")))
                {
                    return "Cloudflare"; // Si se denegó el acceso en HttpClient, requerimos Selenium
                }
                return "Standard";
            }

            // 1. Detección de PerimeterX / Akamai / Captchas generales
            if (html.Contains("px-captcha", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("perimeterx", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("Verify Your Identity", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("Verifica tu identidad", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("sec-cpt", StringComparison.OrdinalIgnoreCase) || // Captcha de Akamai
                html.Contains("securitas", StringComparison.OrdinalIgnoreCase) ||
                (title != null && (title.Contains("Verify Your Identity", StringComparison.OrdinalIgnoreCase) || 
                                   title.Contains("Verifica tu identidad", StringComparison.OrdinalIgnoreCase))))
            {
                return "Cloudflare";
            }

            // 2. Detección de desafíos de Cloudflare (IUAM, Turnstile, etc.)
            if (html.Contains("cloudflare-challenge", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("__cf_bm", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("cf-ray", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("Ray ID:", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) ||
                (title != null && (title.Contains("Attention Required!", StringComparison.OrdinalIgnoreCase) || 
                                   title.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase))))
            {
                return "Cloudflare";
            }

            // 3. Detección de SPA vacío / Requiere JavaScript (Heavy-JS)
            if (html.Contains("You need to enable JavaScript to run this app", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("javascript está deshabilitado", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("enable javascript", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("activar javascript", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("<noscript>", StringComparison.OrdinalIgnoreCase))
            {
                return "Heavy-JS";
            }

            // Detección de contenedores vacíos típicos de SPAs (Angular, React, Vue) sin renderizado de servidor
            if (html.Contains("<div id=\"app\"></div>", StringComparison.OrdinalIgnoreCase) || 
                html.Contains("<div id=\"root\"></div>", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("<app-root></app-root>", StringComparison.OrdinalIgnoreCase))
            {
                return "Heavy-JS";
            }

            // Por defecto, se mantiene la estrategia básica
            return "Standard";
        }
    }
}
