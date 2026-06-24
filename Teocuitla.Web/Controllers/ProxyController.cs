using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Teocuitla.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ProxyController> _logger;

        public ProxyController(IHttpClientFactory httpClientFactory, ILogger<ProxyController> logger)
        {
            _httpClientFactory = httpClientFactory;
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

            try
            {
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Brotli,
                    AllowAutoRedirect = true
                };

                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                var response = await client.GetAsync(uri);
                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, $"Error al obtener la página de destino: {response.ReasonPhrase}");
                }

                var htmlBytes = await response.Content.ReadAsByteArrayAsync();
                var html = Encoding.UTF8.GetString(htmlBytes);

                // Inyectar etiqueta <base> en el head para resolver recursos relativos automáticamente
                var baseTag = $"<base href=\"{uri.GetLeftPart(UriPartial.Authority)}{uri.AbsolutePath}\" />";
                if (html.Contains("<head>", StringComparison.OrdinalIgnoreCase))
                {
                    html = html.Replace("<head>", $"<head>\n{baseTag}", StringComparison.OrdinalIgnoreCase);
                }
                else if (html.Contains("<head ", StringComparison.OrdinalIgnoreCase))
                {
                    // Manejar variantes de head con atributos
                    int index = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
                    int closingIndex = html.IndexOf(">", index);
                    if (closingIndex != -1)
                    {
                        html = html.Insert(closingIndex + 1, $"\n{baseTag}");
                    }
                }

                // Inyectar el script selector interactivo antes de cerrar el body
                var scriptInjector = @"
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el proxy de inyección web.");
                return StatusCode(500, $"Error interno al cargar la página: {ex.Message}");
            }
        }
    }
}
