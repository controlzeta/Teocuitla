using System;
using Xunit;
using HtmlAgilityPack;
using Teocuitla.Shared.Helpers;

namespace Teocuitla.Tests
{
    public class DataNormalizerTests
    {
        [Theory]
        [InlineData("Gold Standard Protein 2kg - 10% OFF", "Gold Standard Protein 2kg")]
        [InlineData("Orgain Protein 1.2kg (Envío Gratis)", "Orgain Protein 1.2kg")]
        [InlineData("  MuscleTech Nitrotech   5lbs  ", "MuscleTech Nitrotech 5lbs")]
        [InlineData("BCAA Powder - Oferta Especial /", "BCAA Powder")]
        public void NormalizeName_CleansNoiseCorrectly(string input, string expected)
        {
            var result = DataNormalizer.NormalizeName(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void NormalizeNameNode_CleansPromotionalNoiseNode()
        {
            var html = "<div>Product Title <span class='badge promo'>15% OFF</span></div>";
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var result = DataNormalizer.NormalizeNameNode(doc.DocumentNode.FirstChild);
            Assert.Equal("Product Title", result);
        }

        [Theory]
        [InlineData("$1,249.99 MXN", 1249.99)]
        [InlineData("Precio: $1.249,99 USD", 1249.99)]
        [InlineData("749.50", 749.50)]
        [InlineData("$1.250", 1.25)]
        [InlineData("Agotado", null)]
        public void NormalizePrice_ParsesFormatsCorrectly(string input, double? expected)
        {
            var result = DataNormalizer.NormalizePrice(input);
            if (expected.HasValue)
            {
                Assert.NotNull(result);
                Assert.Equal((decimal)expected.Value, result.Value);
            }
            else
            {
                Assert.Null(result);
            }
        }

        [Fact]
        public void NormalizePriceNode_IgnoresStruckOriginalPrice()
        {
            var html = "<div><span class='old list-price'>$1,599.00</span> <span class='current-price'>$1,249.99</span></div>";
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var result = DataNormalizer.NormalizePriceNode(doc.DocumentNode.FirstChild);
            Assert.NotNull(result);
            Assert.Equal(1249.99m, result.Value);
        }

        [Theory]
        [InlineData("Out of stock", "instock", false)]
        [InlineData("In Stock", "Some other page text", true)]
        [InlineData(null, "Este producto está agotado por el momento.", false)]
        public void NormalizeStock_DetectsStockAvailability(string? rawStock, string pageText, bool expected)
        {
            var result = DataNormalizer.NormalizeStock(rawStock, pageText);
            Assert.Equal(expected, result);
        }
    }
}
