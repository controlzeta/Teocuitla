using System;
using Xunit;
using HtmlAgilityPack;
using Teocuitla.Shared.Helpers;

namespace Teocuitla.Tests
{
    public class ReadabilityExtractorTests
    {
        [Fact]
        public void PreClean_RemovesScriptsAndInvisibleElements()
        {
            // Arrange
            var html = @"
                <html>
                <head>
                    <title>Test Page</title>
                    <script>console.log('remove me');</script>
                    <style>body { color: red; }</style>
                </head>
                <body>
                    <header>Header content</header>
                    <nav>Menu navigation</nav>
                    <div style='display: none;'>Hidden advertising banner</div>
                    <div id='main-content' class='article-body'>
                        <p>This is the actual readable content of the website that we want to keep.</p>
                        <!-- HTML Comment to remove -->
                    </div>
                    <div class='ad-container'>Improbable content zone</div>
                    <footer>Footer copyright info</footer>
                </body>
                </html>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Act
            ReadabilityExtractor.PreClean(doc);

            // Assert
            var script = doc.DocumentNode.SelectSingleNode("//script");
            var style = doc.DocumentNode.SelectSingleNode("//style");
            var header = doc.DocumentNode.SelectSingleNode("//header");
            var nav = doc.DocumentNode.SelectSingleNode("//nav");
            var footer = doc.DocumentNode.SelectSingleNode("//footer");
            var hidden = doc.DocumentNode.SelectSingleNode("//div[contains(@style, 'display')]");
            var ad = doc.DocumentNode.SelectSingleNode("//div[@class='ad-container']");
            var main = doc.DocumentNode.SelectSingleNode("//div[@id='main-content']");

            Assert.Null(script);
            Assert.Null(style);
            Assert.Null(header);
            Assert.Null(nav);
            Assert.Null(footer);
            Assert.Null(hidden);
            Assert.Null(ad);
            Assert.NotNull(main);
            Assert.Contains("This is the actual readable content", main.InnerText);
        }

        [Fact]
        public void ExtractMainContentText_ExtractsMostReadableNode()
        {
            // Arrange
            var html = @"
                <html>
                <body>
                    <div id='sidebar'>
                        <ul>
                            <li><a href='/link1'>Short Link 1</a></li>
                            <li><a href='/link2'>Short Link 2</a></li>
                            <li><a href='/link3'>Short Link 3</a></li>
                        </ul>
                    </div>
                    <div id='article' class='main-article-content'>
                        <p>This is a long paragraph. It contains multiple sentences and some punctuation marks like commas, which increases the readability score. This block represents the main body of the article or product description, and should be scored higher than the navigation sidebar.</p>
                    </div>
                </body>
                </html>";

            // Act
            var text = ReadabilityExtractor.ExtractMainContentText(html);

            // Assert
            Assert.Contains("This is a long paragraph", text);
            Assert.DoesNotContain("Short Link", text);
        }
    }
}
