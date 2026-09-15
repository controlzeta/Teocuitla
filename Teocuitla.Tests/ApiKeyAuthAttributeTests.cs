using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Teocuitla.Web.Filters;
using Xunit;

namespace Teocuitla.Tests
{
    public class ApiKeyAuthAttributeTests
    {
        [Fact]
        public async Task OnActionExecutionAsync_ReturnsUnauthorized_WhenHeaderIsMissing()
        {
            // Arrange
            var attribute = new ApiKeyAuthAttribute();
            var serviceCollection = new ServiceCollection();
            var inMemorySettings = new Dictionary<string, string?> { { "Scraping:ApiKey", "TestSecretKey" } };
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
            serviceCollection.AddSingleton(configuration);
            var serviceProvider = serviceCollection.BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());

            bool nextCalled = false;
            ActionExecutionDelegate next = () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
            };

            // Act
            await attribute.OnActionExecutionAsync(context, next);

            // Assert
            Assert.False(nextCalled);
            Assert.NotNull(context.Result);
            var objectResult = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal(401, objectResult.StatusCode);
        }

        [Fact]
        public async Task OnActionExecutionAsync_ReturnsUnauthorized_WhenKeyIsInvalid()
        {
            // Arrange
            var attribute = new ApiKeyAuthAttribute();
            var inMemorySettings = new Dictionary<string, string?> { { "Scraping:ApiKey", "ExpectedKey123" } };
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(configuration);
            var serviceProvider = serviceCollection.BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
            httpContext.Request.Headers["X-Api-Key"] = "WrongKey456";
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());

            bool nextCalled = false;
            ActionExecutionDelegate next = () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
            };

            // Act
            await attribute.OnActionExecutionAsync(context, next);

            // Assert
            Assert.False(nextCalled);
            Assert.NotNull(context.Result);
            var objectResult = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal(401, objectResult.StatusCode);
        }

        [Fact]
        public async Task OnActionExecutionAsync_ProceedsToNext_WhenKeyIsValid()
        {
            // Arrange
            var attribute = new ApiKeyAuthAttribute();
            var inMemorySettings = new Dictionary<string, string?> { { "Scraping:ApiKey", "SuperSecretToken" } };
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(configuration);
            var serviceProvider = serviceCollection.BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
            httpContext.Request.Headers["X-Api-Key"] = "SuperSecretToken";
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());

            bool nextCalled = false;
            ActionExecutionDelegate next = () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
            };

            // Act
            await attribute.OnActionExecutionAsync(context, next);

            // Assert
            Assert.True(nextCalled);
            Assert.Null(context.Result);
        }
    }
}
