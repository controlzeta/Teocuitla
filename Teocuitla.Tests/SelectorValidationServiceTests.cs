using Teocuitla.Shared.Models;
using Teocuitla.Worker.Services;
using Xunit;

namespace Teocuitla.Tests
{
    public class SelectorValidationServiceTests
    {
        private readonly SelectorValidationService _service;

        public SelectorValidationServiceTests()
        {
            _service = new SelectorValidationService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SelectorValidationService>.Instance);
        }

        [Fact]
        public void ValidateSiteSelectors_ReturnsTrue_WhenSelectorsAreValidXPathOrCss()
        {
            var site = new CatalogoSitio
            {
                Nombre = "TestSite",
                UrlBase = "https://example.com",
                SelectorPrecioXPath = "//span[@class='price']",
                SelectorStockXPath = ".stock-status",
                SelectorNombreXPath = "//h1[@id='title']",
                SelectorProductoXPath = "div.product-card"
            };

            bool result = _service.ValidateSiteSelectors(site, out var invalidName, out var invalidValue);

            Assert.True(result);
            Assert.Null(invalidName);
            Assert.Null(invalidValue);
        }

        [Fact]
        public void ValidateSiteSelectors_ReturnsFalse_WhenPriceSelectorIsMalformed()
        {
            var site = new CatalogoSitio
            {
                Nombre = "TestSite",
                UrlBase = "https://example.com",
                SelectorPrecioXPath = "//span[@class='price'", // Corchete sin cerrar en XPath
                SelectorStockXPath = ".stock-status"
            };

            bool result = _service.ValidateSiteSelectors(site, out var invalidName, out var invalidValue);

            Assert.False(result);
            Assert.Equal("Precio", invalidName);
            Assert.Equal("//span[@class='price'", invalidValue);
        }

        [Fact]
        public void ValidateSiteSelectors_ReturnsTrue_WhenSelectorsAreEmpty()
        {
            var site = new CatalogoSitio
            {
                Nombre = "EmptySite",
                UrlBase = "https://example.com"
            };

            bool result = _service.ValidateSiteSelectors(site, out var invalidName, out var invalidValue);

            Assert.True(result);
            Assert.Null(invalidName);
            Assert.Null(invalidValue);
        }
    }
}
