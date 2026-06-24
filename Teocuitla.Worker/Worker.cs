using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Teocuitla.Shared.Dtos;
using Teocuitla.Shared.Models;
using Teocuitla.Shared.Data;
using Teocuitla.Worker.Services;

namespace Teocuitla.Worker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ScraperService _scraperService;
        private readonly ProxyService _proxyService;
        private readonly IHttpClientFactory _httpClientFactory;

        public Worker(
            ILogger<Worker> logger,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ScraperService scraperService,
            ProxyService proxyService,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _scraperService = scraperService;
            _proxyService = proxyService;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker de Scraping Teocuitla iniciado en segundo plano.");

            int maxParallel = _configuration.GetValue<int>("Scraping:MaxParallelScrapers", 3);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 1. Obtener tareas pendientes de la base de datos
                    var dueTasks = await GetDueScrapingTasksAsync();
                    
                    if (dueTasks.Count > 0)
                    {
                        _logger.LogInformation("Se encontraron {Count} variantes pendientes de rastreo.", dueTasks.Count);

                        // 2. Crear un canal System.Threading.Channels para procesamiento concurrente
                        var channel = Channel.CreateBounded<VarianteComercial>(new BoundedChannelOptions(dueTasks.Count)
                        {
                            SingleWriter = true,
                            SingleReader = false
                        });

                        // Llenar el canal con las tareas
                        foreach (var task in dueTasks)
                        {
                            await channel.Writer.WriteAsync(task, stoppingToken);
                        }
                        channel.Writer.Complete();

                        // 3. Lanzar consumidores concurrentes (Scrapers en paralelo)
                        var scrapedResults = new ConcurrentBag<IngestionItemDto>();
                        var consumerTasks = new List<Task>();

                        for (int i = 0; i < maxParallel; i++)
                        {
                            consumerTasks.Add(RunScraperConsumerAsync(channel.Reader, scrapedResults, stoppingToken));
                        }

                        // Esperar a que todos los scrapers terminen
                        await Task.WhenAll(consumerTasks);

                        // 4. Enviar el lote consolidado y comprimido a la API Web
                        if (scrapedResults.Count > 0)
                        {
                            await SendScrapedDataBatchAsync(scrapedResults.ToList());
                        }
                    }
                    else
                    {
                        _logger.LogInformation("No hay tareas de rastreo pendientes en este ciclo.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en el ciclo principal del Worker.");
                }

                // Esperar 5 minutos antes de la siguiente verificación de tareas
                _logger.LogInformation("Esperando 5 minutos para el siguiente ciclo de rastreo...");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        private async Task<List<VarianteComercial>> GetDueScrapingTasksAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TeocuitlaDbContext>();

            var now = DateTime.UtcNow;

            // Cargar todas las variantes asociadas a sitios activos
            var allVariantes = await context.VariantesComerciales
                .Include(v => v.CatalogoSitio)
                .Where(v => v.CatalogoSitio != null && v.CatalogoSitio.Activo)
                .ToListAsync();

            // Filtrar en memoria por intervalo
            var due = new List<VarianteComercial>();
            foreach (var v in allVariantes)
            {
                if (v.UltimaActualizacion == null || 
                    v.UltimaActualizacion.Value.AddMinutes(v.CatalogoSitio!.IntervaloMinutos) <= now)
                {
                    due.Add(v);
                }
            }

            return due;
        }

        private async Task RunScraperConsumerAsync(
            ChannelReader<VarianteComercial> reader, 
            ConcurrentBag<IngestionItemDto> results, 
            CancellationToken ct)
        {
            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var variante))
                {
                    if (ct.IsCancellationRequested) break;

                    // Rotar y obtener proxy activo
                    var proxy = await _proxyService.GetNextProxyAsync();

                    try
                    {
                        // Ejecutar el scraping (ligero o pesado según configuración)
                        var scrapResult = await _scraperService.ScrapeAsync(variante, variante.CatalogoSitio!, proxy);

                        if (scrapResult.Exitoso)
                        {
                            // Registrar éxito en el lote de ingesta
                            results.Add(new IngestionItemDto
                            {
                                VarianteComercialId = variante.Id,
                                Precio = scrapResult.Precio,
                                EnStock = scrapResult.EnStock,
                                FechaCaptura = DateTime.UtcNow
                            });

                            if (proxy != null)
                            {
                                // Reportar éxito del proxy para actualizar latencia y reiniciar fallos
                                await _proxyService.ReportProxySuccessAsync(proxy.Id, scrapResult.LatenciaMs);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Scraping falló para variante: {Nombre}.", variante.Nombre);
                            if (proxy != null)
                            {
                                // Reportar fallo del proxy para penalización o baneo automático
                                await _proxyService.ReportProxyFailureAsync(proxy.Id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error crítico en hilo de scraping para variante {Nombre}.", variante.Nombre);
                        if (proxy != null)
                        {
                            await _proxyService.ReportProxyFailureAsync(proxy.Id);
                        }
                    }
                }
            }
        }

        private async Task SendScrapedDataBatchAsync(List<IngestionItemDto> batch)
        {
            _logger.LogInformation("Preparando envío comprimido de un lote de {Count} precios a la API Web...", batch.Count);

            try
            {
                var baseAddress = _configuration["Scraping:BaseAddress"] ?? "http://localhost:5129";
                var apiKey = _configuration["Scraping:WebApiKey"] ?? "TeocuitlaSecretKey123";

                using var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(baseAddress);
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

                // Serializar lote a JSON
                var json = JsonSerializer.Serialize(batch);
                var jsonBytes = Encoding.UTF8.GetBytes(json);

                // Comprimir payload con GZIP para cumplir con el diseño de alto rendimiento
                using var compressedMs = new MemoryStream();
                using (var gzipStream = new GZipStream(compressedMs, CompressionMode.Compress))
                {
                    await gzipStream.WriteAsync(jsonBytes, 0, jsonBytes.Length);
                }
                
                var compressedPayload = compressedMs.ToArray();

                // Crear contenido HTTP indicando codificación gzip
                var byteContent = new ByteArrayContent(compressedPayload);
                byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                byteContent.Headers.ContentEncoding.Add("gzip");

                var response = await client.PostAsync("/api/ingestion/bulk", byteContent);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Lote de {Count} precios transmitido e insertado con éxito en el servidor de la nube.", batch.Count);
                }
                else
                {
                    var errorMsg = await response.Content.ReadAsStringAsync();
                    _logger.LogError("La API Web rechazó el lote. Código: {Code}. Detalle: {Detail}", response.StatusCode, errorMsg);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falla al transmitir lote de ingesta masiva.");
            }
        }
    }
}
