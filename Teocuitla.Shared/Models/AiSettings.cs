namespace Teocuitla.Shared.Models
{
    public class AiSettings
    {
        /// <summary>
        /// Proveedor activo a utilizar: "Gemini", "OpenAI" o "Claude"
        /// </summary>
        public string ActiveProvider { get; set; } = "Gemini";

        /// <summary>
        /// Clave de API para Google Gemini
        /// </summary>
        public string GeminiApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Clave de API para OpenAI (ChatGPT / GPT-4o)
        /// </summary>
        public string OpenAiApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Clave de API para Anthropic Claude
        /// </summary>
        public string ClaudeApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Modelo específico opcional para Gemini (por defecto: gemini-1.5-flash)
        /// </summary>
        public string GeminiModel { get; set; } = "gemini-1.5-flash";

        /// <summary>
        /// Modelo específico opcional para OpenAI (por defecto: gpt-4o-mini)
        /// </summary>
        public string OpenAiModel { get; set; } = "gpt-4o-mini";

        /// <summary>
        /// Modelo específico opcional para Claude (por defecto: claude-3-5-haiku-latest)
        /// </summary>
        public string ClaudeModel { get; set; } = "claude-3-5-haiku-latest";
    }
}
