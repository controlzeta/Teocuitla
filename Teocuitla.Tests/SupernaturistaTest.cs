using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using Teocuitla.Shared.Helpers;
using Teocuitla.Shared.Data;

namespace Teocuitla.Tests
{
    public class SupernaturistaTest
    {
        private readonly ITestOutputHelper _output;

        public SupernaturistaTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void TestExtractionHeuristics_ExtractsExpectedValues()
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

            // 1. Creatina Monohidratada 450 Gr
            var file1 = Path.Combine(htmlDir, "supernaturista.com_2026-07-28_16-41-57.html");
            Assert.True(File.Exists(file1), $"El archivo {file1} no existe.");
            var html1 = File.ReadAllText(file1);
            var result1 = HeuristicExtractor.Extract(html1);

            Assert.Equal("Creatina Monohidratada 450 Gr", result1.Nombre);
            Assert.Equal(565.50m, result1.Precio);
            Assert.Equal("https://supernaturista.com/cdn/shop/files/37355_a41a011c-8188-4190-8265-1f220883368d.jpg?v=1752515803&width=2000", result1.ImagenUrl);
            Assert.Equal("37355", result1.Sku);
            Assert.Equal("BIRDMAN", result1.Marca);

            // 2. Multivitaminico Hombre 30 Cap
            var file2 = Path.Combine(htmlDir, "supernaturista.com_2026-07-28_16-42-09.html");
            Assert.True(File.Exists(file2), $"El archivo {file2} no existe.");
            var html2 = File.ReadAllText(file2);
            var result2 = HeuristicExtractor.Extract(html2);

            Assert.Equal("Multivitaminico Hombre 30 Cap", result2.Nombre);
            Assert.Equal(166.00m, result2.Precio);
            Assert.Equal("https://supernaturista.com/cdn/shop/files/85947_bbc75ca0-191e-492d-ba99-d90452727868.jpg?v=1773878078&width=2000", result2.ImagenUrl);
            Assert.Equal("85947", result2.Sku);
            Assert.Equal("VIDANAT", result2.Marca);
        }
    }
}
