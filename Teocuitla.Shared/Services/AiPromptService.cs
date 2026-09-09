using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Teocuitla.Shared.Models;

namespace Teocuitla.Shared.Services
{
    public class AiPromptService : IAiPromptService
    {
        private readonly HttpClient _httpClient;
        private readonly AiSettings _settings;
        private readonly ILogger<AiPromptService> _logger;

        public AiSettings Settings => _settings;

        public AiPromptService(
            HttpClient httpClient,
            IOptions<AiSettings> settings,
            ILogger<AiPromptService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }


        public async Task<string> ExecutePromptAsync(
            string prompt, 
            string? systemInstruction = null, 
            string? providerOverride = null, 
            CancellationToken cancellationToken = default)
        {
            var provider = (providerOverride ?? _settings.ActiveProvider ?? "Gemini").Trim();

            _logger.LogInformation("Ejecutando prompt con IA usando el proveedor: {Provider}", provider);

            return provider.ToLowerInvariant() switch
            {
                "gemini" => await ExecuteGeminiAsync(prompt, systemInstruction, cancellationToken),
                "openai" => await ExecuteOpenAiAsync(prompt, systemInstruction, cancellationToken),
                "claude" => await ExecuteClaudeAsync(prompt, systemInstruction, cancellationToken),
                _ => throw new NotSupportedException($"El proveedor de IA '{provider}' no está soportado. Use 'Gemini', 'OpenAI' o 'Claude'.")
            };
        }

        public async Task<string> AnalyzePricesAsync(
            string contextData, 
            string queryPrompt, 
            string? providerOverride = null, 
            CancellationToken cancellationToken = default)
        {
            const string systemInstruction = 
                "Eres un analista financiero experto en inteligencia de precios de retail y comercio electrónico para Teocuitla. " +
                "Recibirás datos históricos de precios, competidores, tiendas y stock. " +
                "Tu objetivo es dar respuestas concisas, precisas, detectar anomalías de precios, oportunidades de compra o tendencias.";

            var fullPrompt = $"[DATOS DE PRECIOS Y PRODUCTOS]:\n{contextData}\n\n[SOLICITUD DEL USUARIO]:\n{queryPrompt}";

            return await ExecutePromptAsync(fullPrompt, systemInstruction, providerOverride, cancellationToken);
        }

        #region Proveedor: Gemini
        private async Task<string> ExecuteGeminiAsync(string prompt, string? systemInstruction, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.GeminiApiKey))
            {
                throw new InvalidOperationException("No se ha configurado la GeminiApiKey en AiSettings.");
            }

            var model = string.IsNullOrWhiteSpace(_settings.GeminiModel) ? "gemini-1.5-flash" : _settings.GeminiModel;
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_settings.GeminiApiKey}";

            object requestBody;
            if (!string.IsNullOrWhiteSpace(systemInstruction))
            {
                requestBody = new
                {
                    system_instruction = new
                    {
                        parts = new[] { new { text = systemInstruction } }
                    },
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    }
                };
            }
            else
            {
                requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    }
                };
            }

            var response = await SendJsonAsync(endpoint, requestBody, null, cancellationToken);
            using var doc = JsonDocument.Parse(response);
            
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) && 
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElem))
            {
                return textElem.GetString() ?? string.Empty;
            }

            return response;
        }
        #endregion

        #region Proveedor: OpenAI
        private async Task<string> ExecuteOpenAiAsync(string prompt, string? systemInstruction, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.OpenAiApiKey))
            {
                throw new InvalidOperationException("No se ha configurado la OpenAiApiKey en AiSettings.");
            }

            var model = string.IsNullOrWhiteSpace(_settings.OpenAiModel) ? "gpt-4o-mini" : _settings.OpenAiModel;
            var endpoint = "https://api.openai.com/v1/chat/completions";

            var messages = new System.Collections.Generic.List<object>();
            if (!string.IsNullOrWhiteSpace(systemInstruction))
            {
                messages.Add(new { role = "system", content = systemInstruction });
            }
            messages.Add(new { role = "user", content = prompt });

            var requestBody = new
            {
                model = model,
                messages = messages
            };

            var headers = new (string Key, string Value)[]
            {
                ("Authorization", $"Bearer {_settings.OpenAiApiKey}")
            };

            var response = await SendJsonAsync(endpoint, requestBody, headers, cancellationToken);
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var contentElem))
            {
                return contentElem.GetString() ?? string.Empty;
            }

            return response;
        }
        #endregion

        #region Proveedor: Claude (Anthropic)
        private async Task<string> ExecuteClaudeAsync(string prompt, string? systemInstruction, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.ClaudeApiKey))
            {
                throw new InvalidOperationException("No se ha configurado la ClaudeApiKey en AiSettings.");
            }

            var model = string.IsNullOrWhiteSpace(_settings.ClaudeModel) ? "claude-3-5-haiku-latest" : _settings.ClaudeModel;
            var endpoint = "https://api.anthropic.com/v1/messages";

            var requestBody = new
            {
                model = model,
                max_tokens = 2048,
                system = systemInstruction,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var headers = new (string Key, string Value)[]
            {
                ("x-api-key", _settings.ClaudeApiKey),
                ("anthropic-version", "2023-06-01")
            };

            var response = await SendJsonAsync(endpoint, requestBody, headers, cancellationToken);
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("content", out var contentArr) &&
                contentArr.GetArrayLength() > 0 &&
                contentArr[0].TryGetProperty("text", out var textElem))
            {
                return textElem.GetString() ?? string.Empty;
            }

            return response;
        }
        #endregion

        private async Task<string> SendJsonAsync(
            string url, 
            object body, 
            (string Key, string Value)[]? customHeaders, 
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            var json = JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            if (customHeaders != null)
            {
                foreach (var (key, value) in customHeaders)
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Error en la llamada a la API de IA ({StatusCode}): {Body}", response.StatusCode, responseText);
                throw new HttpRequestException($"Fallo en llamada a la API de IA (HTTP {(int)response.StatusCode}): {responseText}");
            }

            return responseText;
        }
    }
}
