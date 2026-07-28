using System;
using Xunit;
using Teocuitla.Shared.Helpers;

namespace Teocuitla.Tests
{
    public class HeuristicExtractorTests
    {
        [Fact]
        public void Extract_WithJsonLd_ExtractsPerfectData()
        {
            // Arrange
            var html = @"
                <html>
                <head>
                    <script type='application/ld+json'>
                    {
                        ""@context"": ""https://schema.org"",
                        ""@type"": ""Product"",
                        ""name"": ""Gold Standard Protein 2kg"",
                        ""offers"": {
                            ""@type"": ""Offer"",
                            ""price"": ""1249.99"",
                            ""priceCurrency"": ""MXN"",
                            ""availability"": ""https://schema.org/InStock""
                        }
                    }
                    </script>
                </head>
                <body>
                    <h1>Wrong Name in H1</h1>
                </body>
                </html>";

            // Act
            var result = HeuristicExtractor.Extract(html);

            // Assert
            Assert.Equal("Gold Standard Protein 2kg", result.Nombre);
            Assert.Equal(1249.99m, result.Precio);
            Assert.True(result.EnStock);
            Assert.Equal("JSON-LD (Datos Estructurados)", result.MetodoDeteccion);
        }

        [Fact]
        public void Extract_WithMetaTags_ExtractsDataCorrectly()
        {
            // Arrange
            var html = @"
                <html>
                <head>
                    <meta property='og:title' content='Orgain Organic Protein 1.2kg' />
                    <meta property='product:price:amount' content='749.50' />
                    <meta property='product:availability' content='out of stock' />
                </head>
                <body>
                    <h1>H1 Name</h1>
                </body>
                </html>";

            // Act
            var result = HeuristicExtractor.Extract(html);

            // Assert
            Assert.Equal("Orgain Organic Protein 1.2kg", result.Nombre);
            Assert.Equal(749.50m, result.Precio);
            Assert.False(result.EnStock);
            Assert.Equal("Meta Etiquetas (Open Graph)", result.MetodoDeteccion);
        }

        [Fact]
        public void Extract_WithDomFallback_ExtractsDataUsingHeuristics()
        {
            // Arrange
            var html = @"
                <html>
                <head>
                    <title>My Perfect Shake 2kg | Costco</title>
                </head>
                <body>
                    <h1>My Perfect Shake 2kg</h1>
                    <div class='product-price'>El precio especial es de $649.00 en tienda en linea.</div>
                    <span class='stock-warning'>Este producto se encuentra actualmente agotado para envio.</span>
                </body>
                </html>";

            // Act
            var result = HeuristicExtractor.Extract(html);

            // Assert
            Assert.Equal("My Perfect Shake 2kg", result.Nombre);
            Assert.Equal(649.00m, result.Precio);
            Assert.False(result.EnStock);
            Assert.Equal("Analisis Semantico DOM (Fallback)", result.MetodoDeteccion);
        }

        [Fact]
        public void Extract_WithCostcoRealHtmlFiles_ExtractsExpectedPriceAndImage()
        {
            // Arrange
            string? currentDir = AppContext.BaseDirectory;
            string htmlDir = "";
            while (!string.IsNullOrEmpty(currentDir))
            {
                var tempPath = Path.Combine(currentDir, "html");
                if (Directory.Exists(tempPath))
                {
                    htmlDir = tempPath;
                    break;
                }
                currentDir = Path.GetDirectoryName(currentDir);
            }

            Assert.NotEmpty(htmlDir);

            // 1. Validar el primer archivo de Costco (Precio esperado: 299.00)
            var file1 = System.IO.Path.Combine(htmlDir, "costco.com.mx_2026-07-25_14-09-57.html");
            if (System.IO.File.Exists(file1))
            {
                var html1 = System.IO.File.ReadAllText(file1);
                var result1 = HeuristicExtractor.Extract(html1);

                Assert.Contains("Crema de Avellana", result1.Nombre);
                Assert.Equal(299.00m, result1.Precio);
                Assert.NotNull(result1.ImagenUrl);
                Assert.Contains("/medias/sys_master/products/", result1.ImagenUrl);
            }

            // 2. Validar el segundo archivo de Costco (Precio esperado: 289.00)
            var file2 = System.IO.Path.Combine(htmlDir, "costco.com.mx_2026-07-25_14-11-33.html");
            if (System.IO.File.Exists(file2))
            {
                var html2 = System.IO.File.ReadAllText(file2);
                var result2 = HeuristicExtractor.Extract(html2);

                Assert.Contains("Café Chiapas", result2.Nombre);
                Assert.Equal(369.00m, result2.Precio);
                Assert.NotNull(result2.ImagenUrl);
                Assert.Contains("/medias/sys_master/products/", result2.ImagenUrl);
            }
        }

        [Fact]
        public void Extract_WithRealHtmlFiles_FromHtmlFolder()
        {
            string? currentDir = AppContext.BaseDirectory;
            string htmlDir = "";
            while (!string.IsNullOrEmpty(currentDir))
            {
                var tempPath = Path.Combine(currentDir, "html");
                if (Directory.Exists(tempPath))
                {
                    htmlDir = tempPath;
                    break;
                }
                currentDir = Path.GetDirectoryName(currentDir);
            }

            Assert.NotEmpty(htmlDir);

            var amazonFile = Path.Combine(htmlDir, "amazon.com.mx_2026-07-28_15-45-18.html");
            var liverpoolFile = Path.Combine(htmlDir, "liverpool.com.mx_2026-07-28_15-53-15.html");

            Assert.True(File.Exists(amazonFile), "Amazon file does not exist");
            Assert.True(File.Exists(liverpoolFile), "Liverpool file does not exist");

            var amazonHtml = File.ReadAllText(amazonFile);
            var amazonResult = HeuristicExtractor.Extract(amazonHtml);

            var liverpoolHtml = File.ReadAllText(liverpoolFile);
            var liverpoolResult = HeuristicExtractor.Extract(liverpoolHtml);

            // Assert Amazon
            Assert.Contains("Odyssey G5", amazonResult.Nombre);
            Assert.Equal(5908.02m, amazonResult.Precio);
            Assert.Equal("https://m.media-amazon.com/images/I/81Pm4yGtiYL._AC_SX679_.jpg", amazonResult.ImagenUrl);
            Assert.True(amazonResult.EnStock);

            // Assert Liverpool
            Assert.Contains("Centro de lavado", liverpoolResult.Nombre);
            Assert.Equal(29779.40m, liverpoolResult.Precio);
            Assert.Contains("1158330721", liverpoolResult.ImagenUrl);
            Assert.True(liverpoolResult.EnStock);
        }
    }
}
