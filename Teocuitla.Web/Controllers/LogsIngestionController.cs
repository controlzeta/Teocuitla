using System;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Teocuitla.Web.Controllers
{
    [ApiController]
    [Route("api/logs")]
    public class LogsIngestionController : ControllerBase
    {
        private readonly ILogger<LogsIngestionController> _logger;

        public LogsIngestionController(ILogger<LogsIngestionController> logger)
        {
            _logger = logger;
        }

        [HttpPost]
        public IActionResult Ingest([FromBody] JsonElement body)
        {
            try
            {
                // El sink HTTP de Serilog puede enviar los eventos dentro de un objeto con una propiedad "events"
                // o como un array de eventos directamente.
                JsonElement eventsArray;
                if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("events", out var evs) && evs.ValueKind == JsonValueKind.Array)
                {
                    eventsArray = evs;
                }
                else if (body.ValueKind == JsonValueKind.Array)
                {
                    eventsArray = body;
                }
                else if (body.ValueKind == JsonValueKind.Object)
                {
                    // Un solo evento
                    ProcessEvent(body);
                    return Ok();
                }
                else
                {
                    return BadRequest("Formato de log no soportado.");
                }

                foreach (var ev in eventsArray.EnumerateArray())
                {
                    ProcessEvent(ev);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                // Escribir en consola para evitar bucles infinitos en caso de que falle el propio logging
                Console.WriteLine($"Error crítico en LogsIngestionController: {ex.Message}");
                return StatusCode(500, $"Error al procesar logs: {ex.Message}");
            }
        }

        private void ProcessEvent(JsonElement ev)
        {
            // Extraer mensaje
            string message = string.Empty;
            if (ev.TryGetProperty("@m", out var mProp))
            {
                message = mProp.GetString() ?? string.Empty;
            }
            else if (ev.TryGetProperty("@mt", out var mtProp))
            {
                message = mtProp.GetString() ?? string.Empty;
            }
            else if (ev.TryGetProperty("message", out var msgProp))
            {
                message = msgProp.GetString() ?? string.Empty;
            }

            // Extraer nivel de log
            string levelStr = "Information";
            if (ev.TryGetProperty("@l", out var lProp))
            {
                levelStr = lProp.GetString() ?? "Information";
            }
            else if (ev.TryGetProperty("level", out var lvlProp))
            {
                levelStr = lvlProp.GetString() ?? "Information";
            }

            var serilogLevel = MapToSerilogLevel(levelStr);

            // Extraer propiedades enriquecidas de trazabilidad
            string? jobId = ev.TryGetProperty("JobId", out var jProp) ? jProp.GetString() : null;
            string? traceId = ev.TryGetProperty("TraceId", out var tProp) ? tProp.GetString() : null;
            string? productSku = ev.TryGetProperty("ProductSKU", out var sProp) ? sProp.GetString() : null;

            // Extraer detalles de la excepción si están presentes en la traza
            Exception? exception = null;
            if (ev.TryGetProperty("@x", out var xProp))
            {
                var exceptionString = xProp.GetString();
                if (!string.IsNullOrEmpty(exceptionString))
                {
                    exception = new Exception(exceptionString);
                }
            }

            // Crear el logger enriquecido de Serilog directamente para evitar que ASP.NET Core sobreescriba el SourceContext
            var serilogLogger = Serilog.Log
                .ForContext("SourceContext", "Teocuitla.Worker");

            if (!string.IsNullOrEmpty(jobId))
            {
                serilogLogger = serilogLogger.ForContext("JobId", jobId);
            }
            if (!string.IsNullOrEmpty(traceId))
            {
                serilogLogger = serilogLogger.ForContext("TraceId", traceId);
            }
            if (!string.IsNullOrEmpty(productSku))
            {
                serilogLogger = serilogLogger.ForContext("ProductSKU", productSku);
            }

            // Escribir el log con el formato estructurado conservando el origen y la excepción
            serilogLogger.Write(serilogLevel, exception, "[Worker] {Message}", message);
        }

        private LogEventLevel MapToSerilogLevel(string level)
        {
            return level.ToLower() switch
            {
                "verbose" or "v" => LogEventLevel.Verbose,
                "debug" or "d" => LogEventLevel.Debug,
                "information" or "info" or "i" => LogEventLevel.Information,
                "warning" or "warn" or "w" => LogEventLevel.Warning,
                "error" or "err" or "e" => LogEventLevel.Error,
                "fatal" or "f" => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };
        }
    }
}
