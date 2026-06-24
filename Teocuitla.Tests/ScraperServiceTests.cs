using System;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Teocuitla.Worker.Services;

namespace Teocuitla.Tests
{
    public class ScraperServiceTests
    {
        private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
        private readonly Mock<ILogger<ScraperService>> _loggerMock;
        private readonly ScraperService _scraperService;

        public ScraperServiceTests()
        {
            _httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _loggerMock = new Mock<ILogger<ScraperService>>();
            _scraperService = new ScraperService(_httpClientFactoryMock.Object, _loggerMock.Object);
        }

        [Theory]
        [InlineData("123", 123.00)]
        [InlineData("123.45", 123.45)]
        [InlineData("  $1,234.56 ", 1234.56)]
        [InlineData("Precio: $1,299.00 MXN", 1299.00)]
        [InlineData("1.250,75", 1250.75)]
        [InlineData("1299,50", 1299.50)]
        [InlineData("$ 9.99", 9.99)]
        [InlineData("  $ 45,200  ", 45200.00)]
        public void ParsePrice_WithValidFormats_ReturnsExpectedDecimal(string input, decimal expected)
        {
            // Act
            var result = ScraperService.ParsePrice(input);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expected, result.Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Agotado")]
        [InlineData("Sin stock")]
        [InlineData("Precio a consultar")]
        [InlineData("abc")]
        public void ParsePrice_WithInvalidFormats_ReturnsNull(string? input)
        {
            // Act
            var result = ScraperService.ParsePrice(input);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            // Assert
            Assert.NotNull(_scraperService);
        }
    }
}
