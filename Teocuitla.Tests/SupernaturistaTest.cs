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

            // 1. Archivo 1
            var file1 = Path.Combine(htmlDir, "supernaturista.com_2026-07-25_17-31-39.html");
            if (File.Exists(file1))
            {
                var html1 = File.ReadAllText(file1);
                var result1 = HeuristicExtractor.Extract(html1);

                Assert.Equal("Citrato De Magnesio Con Zinc 60 Tab", result1.Nombre);
                Assert.Equal(178.00m, result1.Precio);
                Assert.Equal("https://supernaturista.com/cdn/shop/files/86371_27cb027d-61eb-4aa0-b9c0-27a2a353fe32.jpg?v=1755907706&width=200", result1.ImagenUrl);
            }

            // 2. Archivo 2
            var file2 = Path.Combine(htmlDir, "supernaturista.com_2026-07-25_17-32-13.html");
            if (File.Exists(file2))
            {
                var html2 = File.ReadAllText(file2);
                var result2 = HeuristicExtractor.Extract(html2);

                Assert.Equal("Isolate Whey Chocolate 2.310 K", result2.Nombre);
                Assert.Equal(1044.65m, result2.Precio);
                Assert.Equal("https://supernaturista.com/cdn/shop/files/78859_ae533939-19ec-4dfb-aa6b-d77798c9fce4.jpg?v=1754342454&width=200", result2.ImagenUrl);
            }
        }
    }
}
