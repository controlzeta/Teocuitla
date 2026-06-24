using Xunit;
using Teocuitla.Shared.Helpers;

namespace Teocuitla.Tests
{
    public class AntibotDetectorTests
    {
        [Fact]
        public void DetectBestStrategy_WithNormalPage_ReturnsStandard()
        {
            // Arrange
            var html = "<html><head><title>Product Name</title></head><body><h1>Standard Product Page</h1><span class='price'>$120.00</span></body></html>";
            
            // Act
            var strategy = AntibotDetector.DetectBestStrategy(html, null, "Product Name");

            // Assert
            Assert.Equal("Standard", strategy);
        }

        [Theory]
        [InlineData("challenge-platform/h/g/orchestrate/js")]
        [InlineData("cf-challenge-container")]
        [InlineData("__cf_bm cookie protection")]
        [InlineData("Ray ID: 7fd49b386c9f2b3e")]
        [InlineData("Please wait while we check your browser... cloudflare")]
        public void DetectBestStrategy_WithCloudflareSignatures_ReturnsCloudflare(string signature)
        {
            // Arrange
            var html = $"<html><head><title>Just a moment...</title></head><body><div>{signature}</div></body></html>";

            // Act
            var strategy = AntibotDetector.DetectBestStrategy(html, null, "Just a moment...");

            // Assert
            Assert.Equal("Cloudflare", strategy);
        }

        [Fact]
        public void DetectBestStrategy_WithCloudflareTitle_ReturnsCloudflare()
        {
            // Arrange
            var html = "<html><head><title>Attention Required! | Cloudflare</title></head><body></body></html>";

            // Act
            var strategy = AntibotDetector.DetectBestStrategy(html, null, "Attention Required! | Cloudflare");

            // Assert
            Assert.Equal("Cloudflare", strategy);
        }

        [Theory]
        [InlineData("px-captcha")]
        [InlineData("perimeterx protection block")]
        [InlineData("class=\"Verifica-tu-identida\"")]
        public void DetectBestStrategy_WithPerimeterXSignatures_ReturnsCloudflare(string signature)
        {
            // Arrange
            var html = $"<html><head><title>Verify Your Identity</title></head><body><div>{signature}</div></body></html>";

            // Act
            var strategy = AntibotDetector.DetectBestStrategy(html, null, "Verify Your Identity");

            // Assert
            Assert.Equal("Cloudflare", strategy);
        }

        [Fact]
        public void DetectBestStrategy_WithPerimeterXTitle_ReturnsCloudflare()
        {
            // Arrange
            var html = "<html><head><title>Verifica tu identidad</title></head><body></body></html>";

            // Act
            var strategy = AntibotDetector.DetectBestStrategy(html, null, "Verifica tu identidad");

            // Assert
            Assert.Equal("Cloudflare", strategy);
        }

        [Theory]
        [InlineData("sec-cpt")]
        [InlineData("securitas akamai challenge")]
        public void DetectBestStrategy_WithAkamaiSignatures_ReturnsCloudflare(string signature)
        {
            // Arrange
            var html = $"<html><body><div>{signature}</div></body></html>";

            // Act
            var strategy = AntibotDetector.DetectBestStrategy(html, null, "Challenge");

            // Assert
            Assert.Equal("Cloudflare", strategy);
        }

        [Theory]
        [InlineData("You need to enable JavaScript to run this app")]
        [InlineData("javascript está deshabilitado en su navegador")]
        [InlineData("please enable javascript to proceed")]
        [InlineData("<noscript>Para ver esta página necesitas activar javascript</noscript>")]
        public void DetectBestStrategy_WithJavaScriptRequiredSignatures_ReturnsHeavyJS(string signature)
        {
            // Arrange
            var html = $"<html><body><div>{signature}</div></body></html>";

            // Act
            var strategy = AntibotDetector.DetectBestStrategy(html, null, "My SPA App");

            // Assert
            Assert.Equal("Heavy-JS", strategy);
        }

        [Theory]
        [InlineData("<div id=\"app\"></div>")]
        [InlineData("<div id=\"root\"></div>")]
        [InlineData("<app-root></app-root>")]
        public void DetectBestStrategy_WithEmptySpaContainers_ReturnsHeavyJS(string container)
        {
            // Arrange
            var html = $"<html><head><title>SPA App</title></head><body>{container}</body></html>";

            // Act
            var strategy = AntibotDetector.DetectBestStrategy(html, null, "SPA App");

            // Assert
            Assert.Equal("Heavy-JS", strategy);
        }

        [Theory]
        [InlineData("Error HTTP 403: Forbidden")]
        [InlineData("Unauthorized access")]
        [InlineData("Access Denied by administrator")]
        public void DetectBestStrategy_WithEmptyHtmlButBlockError_ReturnsCloudflare(string errorMsg)
        {
            // Act
            var strategy = AntibotDetector.DetectBestStrategy("", errorMsg, null);

            // Assert
            Assert.Equal("Cloudflare", strategy);
        }
    }
}
