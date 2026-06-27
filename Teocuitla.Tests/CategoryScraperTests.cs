using System;
using Xunit;
using HtmlAgilityPack;
using Teocuitla.Shared.Helpers;

namespace Teocuitla.Tests
{
    public class CategoryScraperTests
    {
        [Fact]
        public void DetectSelectorsHeuristic_WithDirectSiblingsGrid_DetectsCorrectly()
        {
            // Arrange - Una cuadrícula estándar donde los productos son hermanos directos
            var html = @"
                <html>
                <body>
                    <div id='grid-container'>
                        <div class='product-card'>
                            <a href='/producto/1'>
                                <img src='/img/prod1.jpg' class='product-image' />
                                <h3 class='product-title'>Laptop HP Pavilion</h3>
                                <span class='price'>$12,499.00</span>
                            </a>
                        </div>
                        <div class='product-card'>
                            <a href='/producto/2'>
                                <img src='/img/prod2.jpg' class='product-image' />
                                <h3 class='product-title'>Laptop Dell Inspiron</h3>
                                <span class='price'>$14,999.00</span>
                            </a>
                        </div>
                        <div class='product-card'>
                            <a href='/producto/3'>
                                <img src='/img/prod3.jpg' class='product-image' />
                                <h3 class='product-title'>MacBook Air M2</h3>
                                <span class='price'>$22,999.00</span>
                            </a>
                        </div>
                    </div>
                </body>
                </html>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Act
            var selectors = CategoryScraper.DetectSelectorsHeuristic(doc, "https://tienda.com");

            // Assert
            Assert.NotNull(selectors);
            Assert.Contains("grid-container", selectors.Container);
            Assert.Contains("div", selectors.Container);
            Assert.Contains("a", selectors.Container);
            Assert.Contains("img", selectors.Container);
        }

        [Fact]
        public void DetectSelectorsHeuristic_WithWrappedGridColumns_DetectsCorrectly()
        {
            // Arrange - Cada tarjeta de producto está envuelta en una columna (grilla clásica Bootstrap/Tailwind)
            var html = @"
                <html>
                <body>
                    <div class='row' id='catalog-row'>
                        <div class='col-md-4'>
                            <div class='card'>
                                <a href='/p/1' class='link'>
                                    <img src='1.jpg' />
                                    <h2>Producto A</h2>
                                    <div class='amount'>$100.00</div>
                                </a>
                            </div>
                        </div>
                        <div class='col-md-4'>
                            <div class='card'>
                                <a href='/p/2' class='link'>
                                    <img src='2.jpg' />
                                    <h2>Producto B</h2>
                                    <div class='amount'>$200.00</div>
                                </a>
                            </div>
                        </div>
                        <div class='col-md-4'>
                            <div class='card'>
                                <a href='/p/3' class='link'>
                                    <img src='3.jpg' />
                                    <h2>Producto C</h2>
                                    <div class='amount'>$300.00</div>
                                </a>
                            </div>
                        </div>
                    </div>
                </body>
                </html>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Act
            var selectors = CategoryScraper.DetectSelectorsHeuristic(doc, "https://tienda.com");

            // Assert
            Assert.NotNull(selectors);
            Assert.NotEmpty(selectors.Container);
            Assert.Contains("catalog-row", selectors.Container);
            Assert.Contains("div/div/a", selectors.Container);
        }

        [Fact]
        public void ExtraerPrecioEstructurado_WithSlashedAndSplitPrices_ExtractsCorrectly()
        {
            // Arrange - Un precio con descuento y precio anterior tachado
            var html = @"
                <div class='price-box'>
                    <span class='old-price'><del>$1,500.00</del></span>
                    <div class='special-price'>
                        <span class='fraction'>1299</span>
                        <span class='cents'>50</span>
                    </div>
                </div>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var priceNode = doc.DocumentNode.SelectSingleNode("//div[@class='price-box']");

            // Act
            var price = CategoryScraper.ExtraerPrecioEstructurado(priceNode);

            // Assert
            Assert.NotNull(price);
            Assert.Equal(1299.50m, price.Value);
        }

        [Fact]
        public void ExtraerPrecioEstructurado_WithPositionalFallback_ExtractsCorrectly()
        {
            // Arrange - Precio sin clases explícitas pero con estructura entera y centavos posicionales
            var html = @"
                <div class='price'>
                    <span class='symbol'>$</span>
                    <span class='val'>2,499</span>
                    <sup class='dec'>90</sup>
                </div>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var priceNode = doc.DocumentNode.SelectSingleNode("//div[@class='price']");

            // Act
            var price = CategoryScraper.ExtraerPrecioEstructurado(priceNode);

            // Assert
            Assert.NotNull(price);
            Assert.Equal(2499.90m, price.Value);
        }

        [Fact]
        public void ExtraerPrecioEstructurado_WithPromoBadgeNoise_ExtractsCorrectly()
        {
            // Arrange - Un precio con un descuento del 15% que podría confundir al extractor posicional si no se remueve
            var html = @"
                <div class='price-box'>
                    <span class='discount-badge'>15% OFF</span>
                    <span class='price-amount'>1299</span>
                    <sup class='price-cents'>50</sup>
                </div>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var priceNode = doc.DocumentNode.SelectSingleNode("//div[@class='price-box']");

            // Act
            var price = CategoryScraper.ExtraerPrecioEstructurado(priceNode);

            // Assert
            Assert.NotNull(price);
            Assert.Equal(1299.50m, price.Value);
        }

        [Fact]
        public void ExtraerMejorImagen_WithCssBackgroundImage_ExtractsCorrectly()
        {
            // Arrange - Elemento que usa background-image en su inline style
            var html = "<div class='bg-img' style='background-image: url(\"https://tienda.com/media/prod.jpg\"); width:100px;'></div>";
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var imgNode = doc.DocumentNode.SelectSingleNode("//div");

            // Act
            var url = CategoryScraper.ExtraerMejorImagen(imgNode);

            // Assert
            Assert.Equal("https://tienda.com/media/prod.jpg", url);
        }

        [Fact]
        public void ExtraerMejorImagen_WithResponsivePicture_ExtractsCorrectly()
        {
            // Arrange - Elemento picture con sources de alta resolución
            var html = @"
                <picture>
                    <source srcset='prod_1200.jpg 1200w, prod_800.jpg 800w' media='(min-width: 800px)' />
                    <img src='prod_fallback.jpg' class='img' />
                </picture>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var imgNode = doc.DocumentNode.SelectSingleNode("//img");

            // Act
            var url = CategoryScraper.ExtraerMejorImagen(imgNode);

            // Assert
            Assert.Equal("prod_1200.jpg", url);
        }

        [Fact]
        public void ExtraerNombreLimpio_WithPlainTextPromoNoise_CleansCorrectly()
        {
            // Arrange - Nombre de producto con insignias promocionales y envío gratis mezclados
            var html = @"
                <h3 class='title'>
                    Audífonos Inalámbricos Sony WH-1000XM4
                    <span class='badge'>15% OFF</span>
                    - Envío gratis
                </h3>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var nameNode = doc.DocumentNode.SelectSingleNode("//h3");

            // Act
            var name = CategoryScraper.ExtraerNombreLimpio(nameNode);

            // Assert
            // El título debe quedar limpio de "15% OFF" (span) y "- Envío gratis" (texto plano al final)
            Assert.Equal("Audífonos Inalámbricos Sony WH-1000XM4", name);
        }

        [Theory]
        [InlineData("javascript:void(0)", false)]
        [InlineData("javascript:quickView(123)", false)]
        [InlineData("#", false)]
        [InlineData("/", false)]
        [InlineData("tel:+123456", false)]
        [InlineData("mailto:ventas@tienda.com", false)]
        [InlineData("https://tienda.com/producto-a", true)]
        [InlineData("/p/pantalla-lg-55", true)]
        public void EsEnlaceValido_WithVariousInputs_ReturnsExpected(string input, bool expected)
        {
            // Act
            var result = CategoryScraper.EsEnlaceValido(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void DetectSelectorsHeuristic_WithCostcoMexico_DetectsCorrectly()
        {
            // Arrange - Estructura típica del lister de Costco México (Angular/Spartacus)
            var html = @"
                <html>
                <body>
                    <cx-page-layout class='ProductListPageLayout'>
                        <cx-page-slot>
                            <cx-product-list>
                                <div class='container'>
                                    <div class='row'>
                                        <sip-product-list-item class='col-md-4'>
                                            <a href='/p/1264415' class='thumb'>
                                                <img class='product-image' src='https://www.costco.com.mx/media/1.jpg' />
                                            </a>
                                            <div class='lister-name'>
                                                <a href='/p/1264415'>
                                                    <span>Isopure Proteína Vainilla 2kg</span>
                                                </a>
                                            </div>
                                            <div class='product-price-amount'>
                                                <span>$1,249.00</span>
                                            </div>
                                        </sip-product-list-item>
                                        <sip-product-list-item class='col-md-4'>
                                            <a href='/p/1264416' class='thumb'>
                                                <img class='product-image' src='https://www.costco.com.mx/media/2.jpg' />
                                            </a>
                                            <div class='lister-name'>
                                                <a href='/p/1264416'>
                                                    <span>Orgain Proteína Orgánica 1.2kg</span>
                                                </a>
                                            </div>
                                            <div class='product-price-amount'>
                                                <span>$749.00</span>
                                            </div>
                                        </sip-product-list-item>
                                        <sip-product-list-item class='col-md-4'>
                                            <a href='/p/1264417' class='thumb'>
                                                <img class='product-image' src='https://www.costco.com.mx/media/3.jpg' />
                                            </a>
                                            <div class='lister-name'>
                                                <a href='/p/1264417'>
                                                    <span>Premier Protein Malteada Chocolate 12 pack</span>
                                                </a>
                                            </div>
                                            <div class='product-price-amount'>
                                                <span>$599.00</span>
                                            </div>
                                        </sip-product-list-item>
                                    </div>
                                </div>
                            </cx-product-list>
                        </cx-page-slot>
                    </cx-page-layout>
                </body>
                </html>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Act
            var selectors = CategoryScraper.DetectSelectorsHeuristic(doc, "https://www.costco.com.mx");

            // Assert
            Assert.NotNull(selectors);
            Assert.NotEmpty(selectors.Container);
            // El selector del contenedor debe apuntar a los elementos de Costco (sip-product-list-item)
            Assert.Contains("sip-product-list-item", selectors.Container);
        }
    }
}
