using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Teocuitla.Shared.Data;
using Teocuitla.Shared.Models;

namespace Teocuitla.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDbContextFactory<TeocuitlaDbContext> _dbContextFactory;
        private readonly ILogger<ProxyController> _logger;

        public ProxyController(
            IHttpClientFactory httpClientFactory,
            IDbContextFactory<TeocuitlaDbContext> dbContextFactory,
            ILogger<ProxyController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetPage([FromQuery] string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return BadRequest("URL inválida o no proporcionada.");
            }

            _logger.LogInformation("Cargando página proxyfied para inyección: {Url}", url);

            int maxAttempts = 3;
            int attempt = 0;
            string lastError = string.Empty;

            while (attempt < maxAttempts)
            {
                attempt++;
                RegistroProxy? proxyRecord = null;

                // 1. Cargar un proxy de la base de datos
                try
                {
                    using var context = await _dbContextFactory.CreateDbContextAsync();
                    // Obtener un proxy activo y no baneado, prefiriendo el que lleve más tiempo sin usarse (round-robin)
                    proxyRecord = await context.RegistroProxies
                        .Where(p => p.Activo && !p.Baneado)
                        .OrderBy(p => p.UltimoUso)
                        .FirstOrDefaultAsync();

                    if (proxyRecord != null)
                    {
                        proxyRecord.UltimoUso = DateTime.UtcNow;
                        await context.SaveChangesAsync();
                    }
                }
                catch (Exception dbEx)
                {
                    _logger.LogWarning(dbEx, "No se pudieron cargar proxies de la base de datos. Se intentará conexión directa.");
                }

                // 2. Ejecutar la petición HTTP
                try
                {
                    var handler = new HttpClientHandler
                    {
                        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli,
                        AllowAutoRedirect = true
                    };

                    if (proxyRecord != null)
                    {
                        _logger.LogInformation("Intento {Attempt}/{MaxAttempts}: Usando proxy {Ip}:{Puerto} para {Url}", attempt, maxAttempts, proxyRecord.Ip, proxyRecord.Puerto, url);
                        var webProxy = new WebProxy(proxyRecord.Ip, proxyRecord.Puerto);
                        if (!string.IsNullOrEmpty(proxyRecord.Usuario) && !string.IsNullOrEmpty(proxyRecord.Password))
                        {
                            webProxy.Credentials = new NetworkCredential(proxyRecord.Usuario, proxyRecord.Password);
                        }
                        handler.Proxy = webProxy;
                        handler.UseProxy = true;
                    }
                    else
                    {
                        _logger.LogInformation("Intento {Attempt}/{MaxAttempts}: Usando conexión directa para {Url}", attempt, maxAttempts, url);
                    }

                    using var client = new HttpClient(handler);
                    client.Timeout = TimeSpan.FromSeconds(10); // Timeout ágil de 10s

                    var request = new HttpRequestMessage(HttpMethod.Get, uri);

                    // Configurar cabeceras de navegación idénticas a Google Chrome en Windows para evadir PerimeterX/Akamai
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
                    request.Headers.Add("Accept-Language", "es-419,es;q=0.9,en;q=0.8");
                    request.Headers.Add("Cache-Control", "max-age=0");
                    request.Headers.Add("Upgrade-Insecure-Requests", "1");
                    
                    // Cabeceras de Client Hints indispensables para evadir firewalls modernos
                    request.Headers.Add("Sec-Ch-Ua", "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"");
                    request.Headers.Add("Sec-Ch-Ua-Mobile", "?0");
                    request.Headers.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
                    request.Headers.Add("Sec-Fetch-Dest", "document");
                    request.Headers.Add("Sec-Fetch-Mode", "navigate");
                    request.Headers.Add("Sec-Fetch-Site", "none");
                    request.Headers.Add("Sec-Fetch-User", "?1");

                    var response = await client.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        lastError = $"Error HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                        _logger.LogWarning("Intento {Attempt} fallido: {Error}", attempt, lastError);
                        if (proxyRecord != null)
                        {
                            await ReportProxyFailureAsync(proxyRecord.Id);
                        }
                        continue; // Intentar de nuevo con otro proxy
                    }

                    var htmlBytes = await response.Content.ReadAsByteArrayAsync();
                    var html = Encoding.UTF8.GetString(htmlBytes);

                    // 3. Verificar si caímos en un captcha
                    if (IsCaptchaOrBlock(html))
                    {
                        lastError = "Bloqueo por Captcha/Desafío de Identidad detectado en el contenido HTML.";
                        _logger.LogWarning("Intento {Attempt} bloqueado por captcha.", attempt);
                        if (proxyRecord != null)
                        {
                            await ReportProxyFailureAsync(proxyRecord.Id);
                        }
                        continue; // Intentar de nuevo con otro proxy
                    }

                    // Reportar éxito si se utilizó un proxy
                    if (proxyRecord != null)
                    {
                        await ReportProxySuccessAsync(proxyRecord.Id);
                    }

                    // 4. Limpiar y preparar contenido HTML
                    html = CleanHtmlContent(html);

                    // Inyectar etiqueta <base> e interceptor de red en el head
                    var baseTag = $"<base href=\"{uri.GetLeftPart(UriPartial.Authority)}{uri.AbsolutePath}\" />";
                    var interceptorScript = GetNetworkInterceptorScript(url);
                    var headInjection = $"\n{baseTag}\n{interceptorScript}\n";

                    if (html.Contains("<head>", StringComparison.OrdinalIgnoreCase))
                    {
                        html = html.Replace("<head>", $"<head>{headInjection}", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (html.Contains("<head ", StringComparison.OrdinalIgnoreCase))
                    {
                        int index = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
                        int closingIndex = html.IndexOf(">", index);
                        if (closingIndex != -1)
                        {
                            html = html.Insert(closingIndex + 1, headInjection);
                        }
                    }

                    // Inyectar el script visual interactivo antes de cerrar el body
                    var scriptInjector = GetVisualSelectorScript();

                    if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
                    {
                        html = html.Replace("</body>", $"{scriptInjector}\n</body>", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        html += scriptInjector;
                    }

                    return Content(html, "text/html", Encoding.UTF8);
                }
                catch (TaskCanceledException)
                {
                    lastError = "Tiempo de espera agotado (10 segundos).";
                    _logger.LogWarning("Intento {Attempt} fallido por timeout de red.", attempt);
                    if (proxyRecord != null)
                    {
                        await ReportProxyFailureAsync(proxyRecord.Id);
                    }
                }
                catch (Exception ex)
                {
                    lastError = $"Excepción: {ex.Message}";
                    _logger.LogError(ex, "Intento {Attempt} fallido con excepción.", attempt);
                    if (proxyRecord != null)
                    {
                        await ReportProxyFailureAsync(proxyRecord.Id);
                    }
                }
            }

            // Si todos los intentos fallaron, retornar error y detalles del bloqueo
            return StatusCode(502, $"No se pudo evadir el captcha tras {maxAttempts} intentos. Último error: {lastError}");
        }

        [HttpPost("manual")]
        public IActionResult LoadManualHtml([FromForm] string html, [FromForm] string? url)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return BadRequest("El contenido HTML no puede estar vacío.");
            }

            _logger.LogInformation("Cargando HTML manual proporcionado por el usuario.");

            // 1. Limpiar y preparar contenido HTML
            html = CleanHtmlContent(html);

            // 2. Si se proporciona una URL de referencia, inyectar base tag e interceptor de red
            if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var baseTag = $"<base href=\"{uri.GetLeftPart(UriPartial.Authority)}{uri.AbsolutePath}\" />";
                var interceptorScript = GetNetworkInterceptorScript(url);
                var headInjection = $"\n{baseTag}\n{interceptorScript}\n";

                if (html.Contains("<head>", StringComparison.OrdinalIgnoreCase))
                {
                    html = html.Replace("<head>", $"<head>{headInjection}", StringComparison.OrdinalIgnoreCase);
                }
                else if (html.Contains("<head ", StringComparison.OrdinalIgnoreCase))
                {
                    int index = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
                    int closingIndex = html.IndexOf(">", index);
                    if (closingIndex != -1)
                    {
                        html = html.Insert(closingIndex + 1, headInjection);
                    }
                }
            }

            // 3. Inyectar el script visual interactivo antes de cerrar el body
            var scriptInjector = GetVisualSelectorScript();

            if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</body>", $"{scriptInjector}\n</body>", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                html += scriptInjector;
            }

            return Content(html, "text/html", Encoding.UTF8);
        }

        private async Task ReportProxyFailureAsync(int proxyId)
        {
            try
            {
                using var context = await _dbContextFactory.CreateDbContextAsync();
                var proxy = await context.RegistroProxies.FindAsync(proxyId);
                if (proxy != null)
                {
                    proxy.FallosAcumulados++;
                    if (proxy.FallosAcumulados >= 5) // Umbral de baneo automático en 5 fallos
                    {
                        proxy.Baneado = true;
                        _logger.LogWarning("Proxy {Ip}:{Puerto} ha alcanzado el límite y ha sido BANEADO automáticamente desde el Selector Visual.", proxy.Ip, proxy.Puerto);
                    }
                    else
                    {
                        _logger.LogInformation("Incrementado contador de fallos para proxy {Ip}:{Puerto} ({Fallos}/5) desde el Selector Visual.", proxy.Ip, proxy.Puerto, proxy.FallosAcumulados);
                    }
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al reportar fallo del proxy {ProxyId} en base de datos.", proxyId);
            }
        }

        private async Task ReportProxySuccessAsync(int proxyId)
        {
            try
            {
                using var context = await _dbContextFactory.CreateDbContextAsync();
                var proxy = await context.RegistroProxies.FindAsync(proxyId);
                if (proxy != null)
                {
                    proxy.FallosAcumulados = 0; // Resetear fallos al tener éxito
                    proxy.Baneado = false;
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al reportar éxito del proxy {ProxyId} en base de datos.", proxyId);
            }
        }

        private bool IsCaptchaOrBlock(string html)
        {
            if (string.IsNullOrEmpty(html)) return false;

            // Firmas conocidas de bloqueos por captcha (Walmart, Sams, Cloudflare, PerimeterX, Akamai)
            var captchaSignatures = new[]
            {
                "px-captcha",          // PerimeterX
                "challenge-platform",  // Cloudflare
                "cf-challenge",        // Cloudflare
                "mantén presionado",   // Walmart/Sams captcha
                "manten presionado",   
                "verifica tu identidad",
                "human challenge",
                "securitas",           // Akamai
                "perimeterx"
            };

            foreach (var signature in captchaSignatures)
            {
                if (html.Contains(signature, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string CleanHtmlContent(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;

            // Lista de trackers y scripts de publicidad pesados que causan demoras de red y cuelgues
            var patternsToRemove = new[]
            {
                "googletagmanager.com",
                "google-analytics.com",
                "connect.facebook.net",
                "facebook.com/tr",
                "hotjar.com",
                "crazyegg.com",
                "doubleclick.net",
                "tiktok.com/embed",
                "pixel.wp.com"
            };

            foreach (var pattern in patternsToRemove)
            {
                // Neutralizar la descarga del script cambiando src a un valor inocuo
                html = System.Text.RegularExpressions.Regex.Replace(
                    html,
                    $@"(<script[^>]*src=[""'])([^""']*{pattern}[^""']*)([""'])",
                    "$1#blocked-tracker$3",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // Neutralizar llamadas inline asociadas a trackers comunes
                if (pattern == "googletagmanager.com")
                {
                    html = System.Text.RegularExpressions.Regex.Replace(
                        html,
                        @"google_tag_manager|gtm\.js",
                        "blocked_gtm",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                if (pattern == "connect.facebook.net")
                {
                    html = System.Text.RegularExpressions.Regex.Replace(
                        html,
                        @"fbq\(\s*['""]init['""]",
                        "console.log('fbq blocked'); //",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }

            // Desactivar redirecciones de Frame-Busting comunes en scripts inline
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"\btop\.location\b|\bwindow\.top\.location\b|\bparent\.location\b|\bwindow\.parent\.location\b",
                "window.self.location",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return html;
        }

        private string GetNetworkInterceptorScript(string targetUrl)
        {
            return @"
<script>
    (function() {
        try {
            const targetUrlStr = " + $"\"{targetUrl}\"" + @";
            const targetBaseUrl = new URL(targetUrlStr);
            const targetOriginUrl = targetBaseUrl.origin;

            function rewriteUrl(url) {
                if (!url) return url;
                
                // Evitar ciclos si ya es una petición al proxy
                if (url.includes('/api/proxy?url=')) {
                    return url;
                }

                // Conservar recursos internos de la plataforma Blazor
                if (url.includes('_framework/') || url.includes('_content/') || url.includes('selector-helper.js')) {
                    return url;
                }

                let absoluteUrl;
                if (url.startsWith('http://') || url.startsWith('https://')) {
                    const parsed = new URL(url);
                    if (parsed.origin === window.location.origin) {
                        // El navegador resolvió una URL relativa a nuestro origen local, la mapeamos al origen remoto
                        absoluteUrl = targetOriginUrl + parsed.pathname + parsed.search + parsed.hash;
                    } else if (parsed.origin === targetOriginUrl) {
                        // Ya es del origen remoto objetivo
                        absoluteUrl = url;
                    } else {
                        // Dejar pasar recursos de terceros (ej. CDNs con CORS habilitado)
                        return url;
                    }
                } else {
                    // Resolver URL relativa usando la URL original del producto como base
                    absoluteUrl = new URL(url, targetUrlStr).href;
                }

                // Redirigir la llamada a través de nuestro proxy inverso para eludir políticas de CORS
                return window.location.origin + '/api/proxy?url=' + encodeURIComponent(absoluteUrl);
            }

            // Monkey-patch global para window.fetch
            const originalFetch = window.fetch;
            window.fetch = function(input, init) {
                if (typeof input === 'string') {
                    input = rewriteUrl(input);
                } else if (input instanceof Request) {
                    const newUrl = rewriteUrl(input.url);
                    input = new Request(newUrl, input);
                }
                return originalFetch.apply(this, [input, init]);
            };

            // Monkey-patch global para XMLHttpRequest (usado por jQuery, Axios, etc.)
            const originalOpen = XMLHttpRequest.prototype.open;
            XMLHttpRequest.prototype.open = function(method, url, async, user, password) {
                if (typeof url === 'string') {
                    url = rewriteUrl(url);
                }
                return originalOpen.apply(this, arguments);
            };
            
            console.log('Teocuitla: Interceptor de red inyectado. Evitando CORS y 404 en recursos dinámicos.');
        } catch (e) {
            console.error('Teocuitla: Error configurando interceptor de red:', e);
        }
    })();
</script>";
        }

        private string GetVisualSelectorScript()
        {
            return @"
<style>
    .teocuitla-hover-highlight {
        outline: 2px dashed #009688 !important;
        cursor: crosshair !important;
        box-shadow: 0 0 8px rgba(0, 150, 136, 0.5) !important;
    }
</style>
<script>
    (function() {
        console.log('Inyector de selectores Teocuitla cargado correctamente.');

        let activeElement = null;

        // Escuchar movimiento del mouse para resaltar elementos bajo el cursor
        document.addEventListener('mouseover', function(e) {
            if (activeElement) {
                activeElement.classList.remove('teocuitla-hover-highlight');
            }
            activeElement = e.target;
            activeElement.classList.add('teocuitla-hover-highlight');
        }, true);

        // Desactivar el hover al salir de la ventana
        document.addEventListener('mouseout', function(e) {
            if (activeElement) {
                activeElement.classList.remove('teocuitla-hover-highlight');
                activeElement = null;
            }
        }, true);

        // Capturar el click de selección
        document.addEventListener('click', function(e) {
            e.preventDefault();
            e.stopPropagation();

            if (activeElement) {
                activeElement.classList.remove('teocuitla-hover-highlight');
            }

            const xpath = getXPath(e.target);
            const css = getCssSelector(e.target);
            const textValue = e.target.textContent || e.target.innerText || '';

            console.log('Elemento seleccionado:', xpath, css);

            // Comunicar la selección a la ventana padre (Blazor App)
            window.parent.postMessage({
                type: 'teocuitla-selector-selected',
                xpath: xpath,
                css: css,
                text: textValue.trim().substring(0, 100)
            }, '*');

            return false;
        }, true);

        // Función recursiva para calcular XPath absoluto/relativo
        function getXPath(element) {
            if (element.id !== '') {
                return 'id(""' + element.id + '"")';
            }
            if (element === document.body) {
                return element.tagName.toLowerCase();
            }
            let ix = 0;
            const siblings = element.parentNode.childNodes;
            for (let i = 0; i < siblings.length; i++) {
                const sibling = siblings[i];
                if (sibling === element) {
                    return getXPath(element.parentNode) + '/' + element.tagName.toLowerCase() + '[' + (ix + 1) + ']';
                }
                if (sibling.nodeType === 1 && sibling.tagName === element.tagName) {
                    ix++;
                }
            }
        }

        // Función recursiva para calcular Selector CSS
        function getCssSelector(el) {
            if (el.tagName.toLowerCase() === 'html') return 'html';
            let str = el.tagName.toLowerCase();
            if (el.id !== '') {
                return '#' + el.id;
            }
            if (el.className !== '') {
                const classes = el.className.split(/\s+/).filter(c => c && !c.includes('teocuitla'));
                if (classes.length > 0) {
                    str += '.' + classes.join('.');
                }
            }
            let child = el;
            let i = 1;
            while (child = child.previousElementSibling) {
                if (child.tagName === el.tagName) i++;
            }
            return getCssSelector(el.parentElement) + ' > ' + str + ':nth-of-type(' + i + ')';
        }
    })();
</script>";
        }
    }
}
