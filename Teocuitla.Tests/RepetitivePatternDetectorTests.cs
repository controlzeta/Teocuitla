using System;
using Xunit;
using HtmlAgilityPack;
using Teocuitla.Shared.Helpers;

namespace Teocuitla.Tests
{
    public class RepetitivePatternDetectorTests
    {
        [Fact]
        public void DetectSelectors_FindsRepeatingProductGrid()
        {
            // Arrange
            var html = @"
                <html>
                <body>
                    <div id='header'>Header menu</div>
                    <div class='container'>
                        <aside class='sidebar'>Side menu filter</aside>
                        <main id='catalog-grid'>
                            <div class='product-card'>
                                <a href='/product/1'>
                                    <img src='/img/prod1.jpg' alt='Product 1' />
                                    <h3>Protein Shake 1kg</h3>
                                </a>
                                <span class='price'>$899.00</span>
                            </div>
                            <div class='product-card'>
                                <a href='/product/2'>
                                    <img src='/img/prod2.jpg' alt='Product 2' />
                                    <h3>Protein Shake 2kg</h3>
                                </a>
                                <span class='price'>$1,599.00</span>
                            </div>
                            <div class='product-card'>
                                <a href='/product/3'>
                                    <img src='/img/prod3.jpg' alt='Product 3' />
                                    <h3>Protein Shake 3kg</h3>
                                </a>
                                <span class='price'>$2,299.00</span>
                            </div>
                        </main>
                    </div>
                </body>
                </html>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Act
            var selectors = RepetitivePatternDetector.DetectSelectors(doc, "https://test.store.com");

            // Assert
            Assert.NotNull(selectors);
            Assert.Contains("catalog-grid", selectors.Container);
            Assert.Contains("product-card", selectors.Container);
            Assert.NotNull(selectors.Nombre);
            Assert.NotNull(selectors.Precio);
            Assert.NotNull(selectors.Link);
            Assert.NotNull(selectors.Imagen);
        }

        [Fact]
        public void DetectSelectors_WithNoRepetitions_ReturnsNull()
        {
            // Arrange
            var html = @"
                <html>
                <body>
                    <div id='product-detail'>
                        <h1>Single Product Name</h1>
                        <span class='price'>$599.00</span>
                        <img src='/img.jpg' />
                    </div>
                </body>
                </html>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Act
            var selectors = RepetitivePatternDetector.DetectSelectors(doc, "https://test.store.com");

            // Assert
            Assert.Null(selectors);
        }
    }
}
