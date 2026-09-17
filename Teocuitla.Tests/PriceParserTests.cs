using Teocuitla.Shared.Helpers;
using Xunit;

namespace Teocuitla.Tests
{
    public class PriceParserTests
    {
        [Theory]
        [InlineData("$9999", 9999)]
        [InlineData("MXN 1,250.50", 1250.50)]
        [InlineData("1.250,50 EUR", 1250.50)]
        [InlineData("999 MXN", 999)]
        [InlineData("£ 89.95", 89.95)]
        public void Parse_WithSupportedFormats_ReturnsFullPrice(string input, double expected)
        {
            var result = PriceParser.Parse(input);

            Assert.NotNull(result);
            Assert.Equal((decimal)expected, result.Price);
        }

        [Theory]
        [InlineData("Agotado")]
        [InlineData("")]
        [InlineData("Precio a consultar")]
        public void Parse_WithoutANumericPrice_ReturnsNull(string input)
        {
            Assert.Null(PriceParser.Parse(input));
        }

        [Fact]
        public void Parse_WithCurrencyCode_ReturnsNormalizedCurrency()
        {
            var result = PriceParser.Parse("USD 1,299.00");

            Assert.NotNull(result);
            Assert.Equal("USD", result.Currency);
            Assert.Equal("1,299.00", result.RawValue);
        }
    }
}
