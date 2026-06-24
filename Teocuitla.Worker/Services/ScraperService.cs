using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Teocuitla.Shared.Models;
using Teocuitla.Shared.Helpers;
using Microsoft.Extensions.Configuration;



namespace Teocuitla.Worker.Services
{
    public class ScraperResult
    {
        public decimal Precio { get; set; }
        public bool EnStock { get; set; }
        public bool Exitoso { get; set; }
        public int LatenciaMs { get; set; }
        public string? HtmlFallido { get; set; }
        public string? ErrorMensaje { get; set; }
    }

    public class ScraperService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ScraperService> _logger;
        private readonly IConfiguration? _configuration;

        public ScraperService(IHttpClientFactory httpClientFactory, ILogger<ScraperService> logger, IConfiguration? configuration = null)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<ScraperResult> ScrapeAsync(VarianteComercial variante, CatalogoSitio sitio, RegistroProxy? proxy)
        {
            var stopwatch = Stopwatch.StartNew();

            // Validar existencia y sintaxis de los selectores antes de iniciar cualquier fase de scraping
            if (string.IsNullOrWhiteSpace(sitio.SelectorPrecioXPath))
            {
                _logger.LogError("El selector de Precio para el sitio '{Sitio}' está vacío y es requerido para el rastreo.", sitio.Nombre);
                return new ScraperResult { Exitoso = false, LatenciaMs = 0 };
            }

            if (!SelectorValidator.IsValidSelector(sitio.SelectorPrecioXPath))
            {
                _logger.LogError("El selector de Precio '{Selector}' para el sitio '{Sitio}' es inválido (sintaxis XPath/CSS incorrecta).", sitio.SelectorPrecioXPath, sitio.Nombre);
                return new ScraperResult { Exitoso = false, LatenciaMs = 0 };
            }
            if (!string.IsNullOrWhiteSpace(sitio.SelectorNombreXPath) && !SelectorValidator.IsValidSelector(sitio.SelectorNombreXPath))
            {
                _logger.LogError("El selector de Nombre '{Selector}' para el sitio '{Sitio}' es inválido (sintaxis XPath/CSS incorrecta).", sitio.SelectorNombreXPath, sitio.Nombre);
                return new ScraperResult { Exitoso = false, LatenciaMs = 0 };
            }
            if (!string.IsNullOrWhiteSpace(sitio.SelectorStockXPath) && !SelectorValidator.IsValidSelector(sitio.SelectorStockXPath))
            {
                _logger.LogError("El selector de Stock '{Selector}' para el sitio '{Sitio}' es inválido (sintaxis XPath/CSS incorrecta).", sitio.SelectorStockXPath, sitio.Nombre);
                return new ScraperResult { Exitoso = false, LatenciaMs = 0 };
            }
            if (!string.IsNullOrWhiteSpace(sitio.SelectorProductoXPath) && !SelectorValidator.IsValidSelector(sitio.SelectorProductoXPath))
            {
                _logger.LogError("El selector de Contenedor '{Selector}' para el sitio '{Sitio}' es inválido (sintaxis XPath/CSS incorrecta).", sitio.SelectorProductoXPath, sitio.Nombre);
                return new ScraperResult { Exitoso = false, LatenciaMs = 0 };
            }

            _logger.LogInformation("Iniciando scraping para variante: {Nombre} ({Url}) en {Sitio}", 
                variante.Nombre, variante.UrlProducto, sitio.Nombre);

            ScraperResult? finalResult = null;

            // Intentar fase ligera (HttpClient + HtmlAgilityPack) si la estrategia es estándar
            if (sitio.EstrategiaEvasion == "Standard")
            {
                finalResult = await ScrapeWithHttpClientAsync(variante, sitio, proxy);
                if (finalResult.Exitoso)
                {
                    stopwatch.Stop();
                    finalResult.LatenciaMs = (int)stopwatch.ElapsedMilliseconds;
                    _logger.LogInformation("Scraping exitoso (Fase Ligera - HttpClient) para {Nombre}. Precio: ${Precio}", variante.Nombre, finalResult.Precio);
                    return finalResult;
                }
                else
                {
                    _logger.LogWarning("Fase ligera (HttpClient) falló para {Nombre}: {Message}. Reintentando con Selenium...", variante.Nombre, finalResult.ErrorMensaje);
                }
            }

            // Fallback o estrategia pesada: Selenium Headless
            try
            {
                finalResult = await ScrapeWithSeleniumAsync(variante, sitio, proxy);
                stopwatch.Stop();
                finalResult.LatenciaMs = (int)stopwatch.ElapsedMilliseconds;
                if (finalResult.Exitoso)
                {
                    _logger.LogInformation("Scraping exitoso (Fase Pesada - Selenium) para {Nombre}. Precio: ${Precio}", variante.Nombre, finalResult.Precio);
                }
                else
                {
                    _logger.LogWarning("Scraping falló con Selenium para {Nombre}. Detalle: {Message}", variante.Nombre, finalResult.ErrorMensaje);
                }
                return finalResult;
            }
            catch (InvalidSelectorException ex)
            {
                _logger.LogError("Error de configuración de selectores para el sitio '{Sitio}': El navegador rechazó el selector por sintaxis inválida. Detalle: {Message}", sitio.Nombre, ex.Message);
                return new ScraperResult { Exitoso = false, LatenciaMs = (int)stopwatch.ElapsedMilliseconds, ErrorMensaje = ex.Message };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falla crítica en scraping con Selenium para la variante {Nombre}.", variante.Nombre);
                return new ScraperResult { Exitoso = false, LatenciaMs = (int)stopwatch.ElapsedMilliseconds, ErrorMensaje = ex.Message };
            }
        }

        private async Task<ScraperResult> ScrapeWithHttpClientAsync(VarianteComercial variante, CatalogoSitio sitio, RegistroProxy? proxy)
        {
            string html = string.Empty;
            try
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
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8");
                client.DefaultRequestHeaders.Add("Accept-Language", "es-MX,es;q=0.9,en;q=0.8");
                client.DefaultRequestHeaders.Add("Sec-Ch-Ua", "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"");
                client.DefaultRequestHeaders.Add("Sec-Ch-Ua-Mobile", "?0");
                client.DefaultRequestHeaders.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
                client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
                client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

                html = await client.GetStringAsync(variante.UrlProducto);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // El selector debe ser XPath en esta fase
                if (string.IsNullOrWhiteSpace(sitio.SelectorPrecioXPath) || !SelectorValidator.EsSelectorXPath(sitio.SelectorPrecioXPath))
                {
                    throw new NotSupportedException("La fase rápida con HttpClient requiere selectores de tipo XPath (iniciados con / o //).");
                }

                var precioNodo = doc.DocumentNode.SelectSingleNode(sitio.SelectorPrecioXPath);
                if (precioNodo == null)
                {
                    throw new Exception("No se localizó el nodo de precio con el selector XPath provisto.");
                }

                var precioTexto = precioNodo.InnerText;
                var precioDecimal = ParsePrice(precioTexto);

                if (!precioDecimal.HasValue)
                {
                    throw new Exception($"No se pudo parsear el precio extraído: '{precioTexto}'");
                }

                // Verificar Stock
                bool enStock = true;
                if (!string.IsNullOrWhiteSpace(sitio.SelectorStockXPath) && SelectorValidator.EsSelectorXPath(sitio.SelectorStockXPath))
                {
                    var stockNodo = doc.DocumentNode.SelectSingleNode(sitio.SelectorStockXPath);
                    if (stockNodo != null)
                    {
                        var stockTexto = stockNodo.InnerText.ToLower();
                        if (stockTexto.Contains("agotado") || stockTexto.Contains("sin stock") || stockTexto.Contains("no disponible"))
                        {
                            enStock = false;
                        }
                    }
                }

                return new ScraperResult
                {
                    Exitoso = true,
                    Precio = precioDecimal.Value,
                    EnStock = enStock
                };
            }
            catch (Exception ex)
            {
                return new ScraperResult
                {
                    Exitoso = false,
                    HtmlFallido = html,
                    ErrorMensaje = ex.Message
                };
            }
        }

        private async Task<ScraperResult> ScrapeWithSeleniumAsync(VarianteComercial variante, CatalogoSitio sitio, RegistroProxy? proxy)
        {
            var options = new ChromeOptions();
            
            // 1. Ofuscación básica de automatización y extensiones
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);
            
            // 2. Argumentos de evasión y rendimiento
            options.AddArgument("--disable-blink-features=AutomationControlled"); // Deshabilitar indicador de Blink
            options.AddArgument("--disable-infobars");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--start-maximized");
            
            // Permitir desactivar el modo headless para ver el navegador en tiempo real
            bool headless = _configuration?.GetValue<bool>("Scraping:Headless", true) ?? true;
            if (headless)
            {
                options.AddArgument("--headless=new"); // Requerido headless v2 para ahorro de recursos
            }
            else
            {
                _logger.LogInformation("[DEBUG] Iniciando Chrome en modo interactivo (ventana visible) para la variante {Nombre}.", variante.Nombre);
            }
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--blink-settings=imagesEnabled=false"); // Deshabilitar imágenes para velocidad
            
            var userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            options.AddArgument($"--user-agent={userAgent}");

            if (proxy != null)
            {
                options.AddArgument($"--proxy-server={proxy.Ip}:{proxy.Puerto}");
            }

            // Iniciar ChromeDriver de forma segura
            using var driver = new ChromeDriver(options);
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(25);
            
            // 3. Ofuscación avanzada de navigator.webdriver vía CDP (UC Mode en C#)
            var cdpParams = new Dictionary<string, object?>
            {
                { "source", @"
                    // Eliminar la firma de automatización de navigator.webdriver
                    const newProto = navigator.__proto__;
                    delete newProto.webdriver;
                    navigator.__proto__ = newProto;

                    // Emular plugins legítimos para simular un navegador físico
                    Object.defineProperty(navigator, 'plugins', {
                        get: () => [
                            { name: 'Chrome PDF Viewer', filename: 'internal-pdf-viewer' },
                            { name: 'Chromium PDF Viewer', filename: 'internal-pdf-viewer' }
                        ]
                    });

                    // Emular idiomas e idioma de preferencia
                    Object.defineProperty(navigator, 'languages', {
                        get: () => ['es-MX', 'es', 'en-US', 'en']
                    });

                    // Forzar consistencia de la plataforma
                    Object.defineProperty(navigator, 'platform', {
                        get: () => 'Win32'
                    });
                " }
            };
            driver.ExecuteCdpCommand("Page.addScriptToEvaluateOnNewDocument", cdpParams);
            
            try
            {
                await Task.Run(() => driver.Navigate().GoToUrl(variante.UrlProducto));

                // 4. Pausa aleatoria inicial para romper linealidad temporal
                await RandomDelayAsync(2, 4);

                // 5. Simular interacción humana (scroll fluido y pausas)
                await SimulateHumanScrollAsync(driver);

                // 6. Pausa aleatoria post-scroll antes del raspado final
                await RandomDelayAsync(1, 2);

                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                // Determinar tipo de selector de forma robusta (XPath o CSS)
                By precioLocator = SelectorValidator.EsSelectorXPath(sitio.SelectorPrecioXPath)
                    ? By.XPath(sitio.SelectorPrecioXPath)
                    : By.CssSelector(sitio.SelectorPrecioXPath);

                var precioElement = wait.Until(d => 
                {
                    var elements = d.FindElements(precioLocator);
                    return elements.Count > 0 ? elements[0] : null;
                });

                if (precioElement == null)
                {
                    throw new Exception("No se encontró el elemento de precio usando Selenium.");
                }

                var precioTexto = precioElement.Text;
                if (string.IsNullOrWhiteSpace(precioTexto))
                {
                    // Intentar leer el contenido de texto si .Text está vacío (común en elementos ocultos o dinámicos)
                    precioTexto = precioElement.GetAttribute("textContent") ?? string.Empty;
                }

                var precioDecimal = ParsePrice(precioTexto);
                if (!precioDecimal.HasValue)
                {
                    throw new Exception($"No se pudo parsear el precio extraído por Selenium: '{precioTexto}'");
                }

                // Verificar Stock
                bool enStock = true;
                if (!string.IsNullOrWhiteSpace(sitio.SelectorStockXPath))
                {
                    try
                    {
                        By stockLocator = SelectorValidator.EsSelectorXPath(sitio.SelectorStockXPath)
                            ? By.XPath(sitio.SelectorStockXPath)
                            : By.CssSelector(sitio.SelectorStockXPath);

                        var stockElements = driver.FindElements(stockLocator);
                        if (stockElements.Count > 0)
                        {
                            var stockElement = stockElements[0];
                            var stockTexto = stockElement.Text.ToLower();
                            if (string.IsNullOrWhiteSpace(stockTexto))
                            {
                                stockTexto = (stockElement.GetAttribute("textContent") ?? string.Empty).ToLower();
                            }

                            if (stockTexto.Contains("agotado") || stockTexto.Contains("sin stock") || stockTexto.Contains("no disponible"))
                            {
                                enStock = false;
                            }
                        }
                    }
                    catch (NoSuchElementException)
                    {
                        // Si el elemento de stock no existe, asumimos que sí hay stock o que el selector no aplica para este caso
                    }
                }

                return new ScraperResult
                {
                    Exitoso = true,
                    Precio = precioDecimal.Value,
                    EnStock = enStock
                };
            }
            catch (Exception ex)
            {
                string pageSource = string.Empty;
                try
                {
                    pageSource = driver.PageSource;
                }
                catch { /* ignore */ }

                return new ScraperResult
                {
                    Exitoso = false,
                    HtmlFallido = pageSource,
                    ErrorMensaje = ex.Message
                };
            }
        }

        public static decimal? ParsePrice(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            try
            {
                // Extraer solo números, comas y puntos
                var clean = Regex.Replace(input, @"[^\d.,]", "").Trim();

                if (string.IsNullOrEmpty(clean)) return null;

                // Si contiene comas y puntos, determinamos cuál es el decimal basándonos en cuál aparece al final
                if (clean.Contains(",") && clean.Contains("."))
                {
                    if (clean.LastIndexOf('.') > clean.LastIndexOf(','))
                    {
                        // El punto está al final (ej: 1,250.75), la coma es separador de miles
                        clean = clean.Replace(",", "");
                    }
                    else
                    {
                        // La coma está al final (ej: 1.250,75), el punto es separador de miles
                        clean = clean.Replace(".", "").Replace(",", ".");
                    }
                }
                else if (clean.Contains(",") && !clean.Contains("."))
                {
                    // Podría ser decimal con coma (Ej: "1299,00") o miles con coma (Ej: "1,299")
                    var parts = clean.Split(',');
                    if (parts.Length == 2 && parts[1].Length == 2)
                    {
                        clean = clean.Replace(",", "."); // Convertir a decimal estándar
                    }
                    else
                    {
                        clean = clean.Replace(",", ""); // Quitar miles
                    }
                }

                if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                {
                    return price;
                }
            }
            catch
            {
                // Ignorar fallos de expresión regular o formateo
            }

            return null;
        }


        private async Task SimulateHumanScrollAsync(ChromeDriver driver)
        {
            try
            {
                var js = (IJavaScriptExecutor)driver;
                var random = new Random();
                
                // Realizar entre 2 y 4 scrolls parciales hacia abajo
                int scrolls = random.Next(2, 5);
                for (int i = 0; i < scrolls; i++)
                {
                    // Desplazamiento de píxeles aleatorio (entre 150 y 350 píxeles)
                    int scrollPixels = random.Next(150, 350);
                    js.ExecuteScript($"window.scrollBy(0, {scrollPixels});");
                    
                    // Pausa aleatoria entre desplazamientos (400ms a 900ms) para simular lectura humana
                    await Task.Delay(random.Next(400, 900));
                }

                // Scroll leve hacia arriba de corrección visual (simulación del foco de lectura)
                if (random.NextDouble() > 0.3)
                {
                    js.ExecuteScript($"window.scrollBy(0, -{random.Next(50, 150)});");
                    await Task.Delay(random.Next(300, 600));
                }
            }
            catch
            {
                // Ignorar fallos de scroll si la página se cierra o cancela
            }
        }

        private async Task RandomDelayAsync(int minSeconds, int maxSeconds)
        {
            var random = new Random();
            int delayMs = random.Next(minSeconds * 1000, maxSeconds * 1000);
            await Task.Delay(delayMs);
        }
    }
}
