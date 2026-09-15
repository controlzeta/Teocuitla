using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Teocuitla.Shared.Data;
using Teocuitla.Shared.Dtos;
using Teocuitla.Web.Services;
using Xunit;

namespace Teocuitla.Tests
{
    public class BulkIngestionServiceTests
    {
        private readonly Mock<ILogger<BulkIngestionService>> _loggerMock;

        public BulkIngestionServiceTests()
        {
            _loggerMock = new Mock<ILogger<BulkIngestionService>>();
        }

        [Fact]
        public async Task ProcessBulkPriceIngestionAsync_ReturnsFalse_WhenItemsListIsNull()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<TeocuitlaDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new TeocuitlaDbContext(options);
            var service = new BulkIngestionService(context, _loggerMock.Object);

            // Act
            var result = await service.ProcessBulkPriceIngestionAsync(null!);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(0, result.ProcessedCount);
            Assert.Equal("El lote de datos está vacío.", result.Message);
        }

        [Fact]
        public async Task ProcessBulkPriceIngestionAsync_ReturnsFalse_WhenItemsListIsEmpty()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<TeocuitlaDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new TeocuitlaDbContext(options);
            var service = new BulkIngestionService(context, _loggerMock.Object);

            // Act
            var result = await service.ProcessBulkPriceIngestionAsync(new List<IngestionItemDto>());

            // Assert
            Assert.False(result.Success);
            Assert.Equal(0, result.ProcessedCount);
            Assert.Equal("El lote de datos está vacío.", result.Message);
        }

        [Fact]
        public async Task ProcessBulkPriceIngestionAsync_ThrowsInvalidOperationException_WhenProviderIsNotSqlServer()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<TeocuitlaDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new TeocuitlaDbContext(options);
            var service = new BulkIngestionService(context, _loggerMock.Object);

            var items = new List<IngestionItemDto>
            {
                new IngestionItemDto { VarianteComercialId = 1, Precio = 100m, EnStock = true, FechaCaptura = DateTime.UtcNow }
            };

            // Act & Assert: en InMemoryDatabase no existe una SqlConnection real y debe lanzar InvalidOperationException controlada
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessBulkPriceIngestionAsync(items));
        }
    }
}
