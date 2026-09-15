using Microsoft.Extensions.Logging;
using Teocuitla.Shared.Helpers;
using Teocuitla.Shared.Models;

namespace Teocuitla.Worker.Services
{
    public interface ISelectorValidationService
    {
        /// <summary>
        /// Valida la sintaxis de todos los selectores configurados para el sitio.
        /// Devuelve true si todos son válidos o están vacíos; false si alguno contiene sintaxis inválida.
        /// </summary>
        bool ValidateSiteSelectors(CatalogoSitio sitio, out string? invalidSelectorName, out string? invalidSelectorValue);
    }

    public class SelectorValidationService : ISelectorValidationService
    {
        private readonly ILogger<SelectorValidationService> _logger;

        public SelectorValidationService(ILogger<SelectorValidationService> logger)
        {
            _logger = logger;
        }

        public bool ValidateSiteSelectors(CatalogoSitio sitio, out string? invalidSelectorName, out string? invalidSelectorValue)
        {
            invalidSelectorName = null;
            invalidSelectorValue = null;

            if (!string.IsNullOrWhiteSpace(sitio.SelectorPrecioXPath) && !SelectorValidator.IsValidSelector(sitio.SelectorPrecioXPath))
            {
                invalidSelectorName = "Precio";
                invalidSelectorValue = sitio.SelectorPrecioXPath;
                _logger.LogError("El selector de Precio '{Selector}' para el sitio '{Sitio}' es inválido (sintaxis XPath/CSS incorrecta).", sitio.SelectorPrecioXPath, sitio.Nombre);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(sitio.SelectorNombreXPath) && !SelectorValidator.IsValidSelector(sitio.SelectorNombreXPath))
            {
                invalidSelectorName = "Nombre";
                invalidSelectorValue = sitio.SelectorNombreXPath;
                _logger.LogError("El selector de Nombre '{Selector}' para el sitio '{Sitio}' es inválido (sintaxis XPath/CSS incorrecta).", sitio.SelectorNombreXPath, sitio.Nombre);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(sitio.SelectorStockXPath) && !SelectorValidator.IsValidSelector(sitio.SelectorStockXPath))
            {
                invalidSelectorName = "Stock";
                invalidSelectorValue = sitio.SelectorStockXPath;
                _logger.LogError("El selector de Stock '{Selector}' para el sitio '{Sitio}' es inválido (sintaxis XPath/CSS incorrecta).", sitio.SelectorStockXPath, sitio.Nombre);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(sitio.SelectorProductoXPath) && !SelectorValidator.IsValidSelector(sitio.SelectorProductoXPath))
            {
                invalidSelectorName = "Contenedor";
                invalidSelectorValue = sitio.SelectorProductoXPath;
                _logger.LogError("El selector de Contenedor '{Selector}' para el sitio '{Sitio}' es inválido (sintaxis XPath/CSS incorrecta).", sitio.SelectorProductoXPath, sitio.Nombre);
                return false;
            }

            return true;
        }
    }
}
