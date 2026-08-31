using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
                Assert.Equal(289.00m, result2.Precio);
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

            var amazonFile = Directory.GetFiles(htmlDir, "amazon.com.mx_*.html").FirstOrDefault() 
                             ?? Path.Combine(htmlDir, "amazon.com.mx_2026-07-28_15-45-18.html");
            var liverpoolFile = Directory.GetFiles(htmlDir, "liverpool.com.mx_*.html").FirstOrDefault()
                                ?? Path.Combine(htmlDir, "liverpool.com.mx_2026-07-28_15-53-15.html");

            if (File.Exists(amazonFile))
            {
                var amazonHtml = File.ReadAllText(amazonFile);
                var amazonResult = HeuristicExtractor.Extract(amazonHtml);
                Assert.NotNull(amazonResult.Nombre);
                if (Path.GetFileName(amazonFile).Contains("2026-08-19"))
                {
                    Assert.Null(amazonResult.Precio);
                }
                else
                {
                    Assert.True(amazonResult.Precio.HasValue && amazonResult.Precio.Value > 0);
                }
            }

            if (File.Exists(liverpoolFile))
            {
                var liverpoolHtml = File.ReadAllText(liverpoolFile);
                var liverpoolResult = HeuristicExtractor.Extract(liverpoolHtml);
                Assert.Contains("Centro de lavado", liverpoolResult.Nombre);
                Assert.Equal(29779.20m, liverpoolResult.Precio);
                Assert.Contains("11583304", liverpoolResult.ImagenUrl);
                Assert.True(liverpoolResult.EnStock);
            }
        }

        [Fact]
        public void TestNewCostcoFiles()
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


            var fileNew = Path.Combine(htmlDir, "liverpool.com.mx_2026-07-30_16-50-57.html");
            if (File.Exists(fileNew))
            {
                var htmlNew = File.ReadAllText(fileNew);
                var resultNew = HeuristicExtractor.Extract(htmlNew);
                Assert.Equal(29779.20m, resultNew.Precio);
            }

            // 1. Nintendo Switch 2 (Rebajado de 12,999.00 a 9,999.00)
            var file1 = Path.Combine(htmlDir, "costco.com.mx_2026-07-30_16-30-48.html");
            if (File.Exists(file1))
            {
                var html1 = File.ReadAllText(file1);
                var result1 = HeuristicExtractor.Extract(html1);
                Assert.Contains("Nintendo Switch 2", result1.Nombre);
                Assert.Equal(9999.00m, result1.Precio);
            }

            // 2. Kirkland Signature Psyllium Plantago (Normal: 169.00)
            var file2 = Path.Combine(htmlDir, "costco.com.mx_2026-07-30_16-31-42.html");
            if (File.Exists(file2))
            {
                var html2 = File.ReadAllText(file2);
                var result2 = HeuristicExtractor.Extract(html2);
                Assert.Contains("Psyllium Plantago", result2.Nombre);
                Assert.Equal(169.00m, result2.Precio);
            }

            // 3. Isopure Vainilla (Normal: 1,999.00)
            var file3 = Path.Combine(htmlDir, "costco.com.mx_2026-07-30_16-32-04.html");
            if (File.Exists(file3))
            {
                var html3 = File.ReadAllText(file3);
                var result3 = HeuristicExtractor.Extract(html3);
                Assert.Contains("Isopure", result3.Nombre);
                Assert.Equal(1999.00m, result3.Precio);
            }
        }

        [Fact]
        public void Extract_AllHtmlFilesInHtmlFolder()
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
            var files = Directory.GetFiles(htmlDir, "*.html");
            Assert.NotEmpty(files);

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var html = File.ReadAllText(file);
                var result = HeuristicExtractor.Extract(html);
                Assert.False(string.IsNullOrWhiteSpace(result.Nombre), $"File {fileName} failed to extract valid Nombre!");
                if (fileName.Contains("2026-08-19"))
                {
                    Assert.Null(result.Precio);
                }
                else
                {
                    Assert.True(!result.EnStock || (result.Precio.HasValue && result.Precio.Value > 0), $"File {fileName} failed to extract valid price/stock! Got Price: {result.Precio}, Stock: {result.EnStock}");
                }
            }
        }

        [Fact]
        public void Test_OutOfStockFile_Extraction()
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

            // 1. Validar producto agotado (Lenovo LOQ Gamer) -> No debe extraer precio
            var pathOos = Path.Combine(htmlDir, "amazon.com.mx_2026-08-19_13-58-10.html");
            var pathNoPrice = Path.Combine(htmlDir, "amazon.com.mx_2026-08-19_13-58-45.html");
            if (!File.Exists(pathOos) || !File.Exists(pathNoPrice))
            {
                return;
            }

            var htmlOos = File.ReadAllText(pathOos);
            var resultOos = HeuristicExtractor.Extract(htmlOos);
            Assert.Contains("Lenovo Laptop Gamer LOQ", resultOos.Nombre);
            Assert.Null(resultOos.Precio);
            Assert.False(resultOos.EnStock);

            // 2. Validar producto sin precio destacado (Finish Abrillantador) -> No debe extraer precio
            var htmlNoPrice = File.ReadAllText(pathNoPrice);
            var resultNoPrice = HeuristicExtractor.Extract(htmlNoPrice);
            Assert.Contains("Finish® Liquido Abrillantador", resultNoPrice.Nombre);
            Assert.Null(resultNoPrice.Precio);
            Assert.True(resultNoPrice.EnStock);
        }
    }
}
