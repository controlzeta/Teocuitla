using System;
using System.Text.Json.Serialization;

namespace Teocuitla.Web.Models
{
    public class LogRecord
    {
        [JsonPropertyName("@t")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("@m")]
        public string? Message { get; set; }

        [JsonPropertyName("@mt")]
        public string? MessageTemplate { get; set; }

        [JsonPropertyName("@l")]
        public string? CompactLevel { get; set; }

        [JsonPropertyName("JobId")]
        public string? JobId { get; set; }

        [JsonPropertyName("TraceId")]
        public string? TraceId { get; set; }

        [JsonPropertyName("ProductSKU")]
        public string? ProductSKU { get; set; }

        [JsonPropertyName("SourceContext")]
        public string? SourceContext { get; set; }

        // Propiedad de conveniencia para mapear el nivel de Serilog Compact JSON
        public string Level => CompactLevel switch
        {
            "v" or "verbose" or "Verbose" => "Verbose",
            "d" or "debug" or "Debug" => "Debug",
            "w" or "warning" or "Warning" or "warn" => "Warning",
            "e" or "error" or "Error" or "err" => "Error",
            "f" or "fatal" or "Fatal" => "Fatal",
            _ => "Information"
        };

        // Propiedad de conveniencia para mostrar el mensaje de log formateado
        public string DisplayMessage => Message ?? MessageTemplate ?? string.Empty;
    }
}
