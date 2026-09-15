using System.Threading.Tasks;
using Teocuitla.Shared.Models;

namespace Teocuitla.Worker.Services
{
    public interface IScrapingEngine
    {
        /// <summary>
        /// Nombre identificador de la estrategia ("Standard", "Heavy-JS", "Cloudflare").
        /// </summary>
        string StrategyName { get; }

        /// <summary>
        /// Ejecuta el proceso de descarga y extracción de la variante en el sitio.
        /// </summary>
        Task<ScraperResult> ExecuteScrapeAsync(VarianteComercial variante, CatalogoSitio sitio, RegistroProxy? proxy);
    }
}
