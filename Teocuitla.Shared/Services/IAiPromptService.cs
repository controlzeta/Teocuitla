using System.Threading;
using System.Threading.Tasks;
using Teocuitla.Shared.Models;

namespace Teocuitla.Shared.Services
{
    public interface IAiPromptService
    {
        /// <summary>
        /// Obtiene la configuración actual de IA.
        /// </summary>
        AiSettings Settings { get; }

        /// <summary>
        /// Ejecuta un prompt simple utilizando el proveedor activo o el especificado.
        /// </summary>
        Task<string> ExecutePromptAsync(string prompt, string? systemInstruction = null, string? providerOverride = null, CancellationToken cancellationToken = default);


        /// <summary>
        /// Analiza información estructurada de precios, variaciones y tendencias para un producto o grupo de productos.
        /// </summary>
        Task<string> AnalyzePricesAsync(string contextData, string queryPrompt, string? providerOverride = null, CancellationToken cancellationToken = default);
    }
}
