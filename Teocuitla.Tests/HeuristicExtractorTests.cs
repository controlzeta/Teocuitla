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
    }
}
