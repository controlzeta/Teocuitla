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

namespace Teocuitla.Worker.Services
{
    public class ScraperResult
    {
        public decimal Precio { get; set; }
        public bool EnStock { get; set; }
        public bool Exitoso { get; set; }
        public int LatenciaMs { get; set; }
    }

    public class ScraperService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ScraperService> _logger;

        public ScraperService(IHttpClientFactory httpClientFactory, ILogger<ScraperService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<ScraperResult> ScrapeAsync(VarianteComercial variante, CatalogoSitio sitio, RegistroProxy? proxy)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Iniciando scraping para variante: {Nombre} ({Url}) en {Sitio}", 
                variante.Nombre, variante.UrlProducto, sitio.Nombre);

            // Intentar fase ligera (HttpClient + HtmlAgilityPack) si la estrategia es estándar
            if (sitio.EstrategiaEvasion == "Standard")
            {
                try
                {
                    var result = await ScrapeWithHttpClientAsync(variante, sitio, proxy);
                    stopwatch.Stop();
                    result.LatenciaMs = (int)stopwatch.ElapsedMilliseconds;
                    if (result.Exitoso)
                    {
                        _logger.LogInformation("Scraping exitoso (Fase Ligera - HttpClient) para {Nombre}. Precio: ${Precio}", variante.Nombre, result.Precio);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Fase ligera (HttpClient) falló para {Nombre}: {Message}. Reintentando con Selenium...", variante.Nombre, ex.Message);
                }
            }

            // Fallback o estrategia pesada: Selenium Headless
            try
            {
                var result = await ScrapeWithSeleniumAsync(variante, sitio, proxy);
                stopwatch.Stop();
                result.LatenciaMs = (int)stopwatch.ElapsedMilliseconds;
                if (result.Exitoso)
                {
                    _logger.LogInformation("Scraping exitoso (Fase Pesada - Selenium) para {Nombre}. Precio: ${Precio}", variante.Nombre, result.Precio);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falla crítica en scraping con Selenium para la variante {Nombre}.", variante.Nombre);
            }

            stopwatch.Stop();
            return new ScraperResult { Exitoso = false, LatenciaMs = (int)stopwatch.ElapsedMilliseconds };
        }

        private async Task<ScraperResult> ScrapeWithHttpClientAsync(VarianteComercial variante, CatalogoSitio sitio, RegistroProxy? proxy)
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
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "es-MX,es;q=0.9,en;q=0.8");

            var html = await client.GetStringAsync(variante.UrlProducto);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // El selector debe ser XPath en esta fase
            if (string.IsNullOrWhiteSpace(sitio.SelectorPrecioXPath) || !sitio.SelectorPrecioXPath.StartsWith("/"))
            {
                throw new NotSupportedException("Fase HttpClient requiere selectores XPath válidos (iniciados con / o //).");
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
            if (!string.IsNullOrWhiteSpace(sitio.SelectorStockXPath) && sitio.SelectorStockXPath.StartsWith("/"))
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

        private async Task<ScraperResult> ScrapeWithSeleniumAsync(VarianteComercial variante, CatalogoSitio sitio, RegistroProxy? proxy)
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless=new"); // Requerido headless v2 para ahorro de recursos
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--blink-settings=imagesEnabled=false"); // Deshabilitar imágenes para velocidad
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            if (proxy != null)
            {
                options.AddArgument($"--proxy-server={proxy.Ip}:{proxy.Puerto}");
            }

            // Iniciar ChromeDriver de forma segura
            using var driver = new ChromeDriver(options);
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(25);
            
            await Task.Run(() => driver.Navigate().GoToUrl(variante.UrlProducto));

            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

            // Determinar tipo de selector (XPath o CSS)
            By precioLocator = sitio.SelectorPrecioXPath.StartsWith("/") || sitio.SelectorPrecioXPath.StartsWith("(")
                ? By.XPath(sitio.SelectorPrecioXPath)
                : By.CssSelector(sitio.SelectorPrecioXPath);

            var precioElement = wait.Until(d => d.FindElement(precioLocator));
            if (precioElement == null)
            {
                throw new Exception("No se encontró el elemento de precio usando Selenium.");
            }

            var precioTexto = precioElement.Text;
            if (string.IsNullOrWhiteSpace(precioTexto))
            {
                // Intentar leer el contenido de texto si .Text está vacío (común en elementos ocultos o dinámicos)
                precioTexto = precioElement.GetAttribute("textContent");
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
                    By stockLocator = sitio.SelectorStockXPath.StartsWith("/")
                        ? By.XPath(sitio.SelectorStockXPath)
                        : By.CssSelector(sitio.SelectorStockXPath);

                    var stockElement = driver.FindElement(stockLocator);
                    if (stockElement != null)
                    {
                        var stockTexto = stockElement.Text.ToLower();
                        if (string.IsNullOrWhiteSpace(stockTexto))
                        {
                            stockTexto = stockElement.GetAttribute("textContent").ToLower();
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

        public static decimal? ParsePrice(string input)
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
    }
}
