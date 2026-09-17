using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Teocuitla.Shared.Data;
using Teocuitla.Shared.Dtos;
using Teocuitla.Web.Controllers;
using Teocuitla.Web.Services;

namespace Teocuitla.Tests
{
    public class ExtensionHeuristicExtractionTests
    {
        [Fact]
        public void ExtractFromExtensionHtml_ReturnsJsonLdExtraction_ForMercadoLibreHtml()
        {
            var controller = CreateController();

            var action = controller.ExtractFromExtensionHtml(new ExtensionHeuristicExtractionRequestDto
            {
                UrlProducto = "https://www.mercadolibre.com.mx/up/MLMU123456789",
                Html = """
                    <script type="application/ld+json">
                    {
                      "@context": "https://schema.org",
                      "@type": "Product",
                      "name": "Filamento Sunlu PETG",
                      "offers": {
                        "@type": "Offer",
                        "price": "245.98",
                        "priceCurrency": "MXN",
                        "availability": "https://schema.org/InStock"
                      }
                    }
                    </script>
                    """
            });

            var ok = Assert.IsType<OkObjectResult>(action);
            var result = Assert.IsType<ExtensionHeuristicExtractionResultDto>(ok.Value);
            Assert.Equal(245.98m, result.Precio);
            Assert.Equal("MXN", result.Moneda);
            Assert.Equal("JSON-LD", result.FuentePrecio);
            Assert.Equal(100, result.ConfianzaPrecio);
        }

        [Fact]
        public void ExtractFromExtensionHtml_ReturnsPayloadTooLarge_WhenHtmlExceedsConfiguredLimit()
        {
            var controller = CreateController(new Dictionary<string, string?>
            {
                ["Ingestion:ExtensionExtraction:MaxHtmlBytes"] = "10"
            });

            var action = controller.ExtractFromExtensionHtml(new ExtensionHeuristicExtractionRequestDto
            {
                UrlProducto = "https://www.example.com/producto",
                Html = "<html>contenido superior al límite</html>"
            });

            var result = Assert.IsType<ObjectResult>(action);
            Assert.Equal(413, result.StatusCode);
        }

        [Fact]
        public void ExtractFromExtensionHtml_ReturnsBadRequest_WhenUrlIsNotHttp()
        {
            var controller = CreateController();

            var action = controller.ExtractFromExtensionHtml(new ExtensionHeuristicExtractionRequestDto
            {
                UrlProducto = "file:///producto.html",
                Html = "<html></html>"
            });

            Assert.IsType<BadRequestObjectResult>(action);
        }

        private static IngestionController CreateController(Dictionary<string, string?>? settings = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
                .Build();
            var contextOptions = new DbContextOptionsBuilder<TeocuitlaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new IngestionController(
                new TeocuitlaDbContext(contextOptions),
                configuration,
                Mock.Of<ILogger<IngestionController>>(),
                new IngestionNotificationService(),
                Mock.Of<IBulkIngestionService>());
        }
    }
}
