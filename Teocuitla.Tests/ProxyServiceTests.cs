using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Teocuitla.Shared.Data;
using Teocuitla.Shared.Models;
using Teocuitla.Worker.Services;

namespace Teocuitla.Tests
{
    public class ProxyServiceTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly TeocuitlaDbContext _dbContext;
        private readonly Mock<ILogger<ProxyService>> _loggerMock;
        private readonly ProxyService _proxyService;

        public ProxyServiceTests()
        {
            // Configurar el contenedor de dependencias con base de datos en memoria
            var services = new ServiceCollection();
            
            // IMPORTANTE: Definir el nombre de la base de datos fuera de la expresión lambda
            // para asegurar que todas las instancias del DbContext compartan la misma base de datos en memoria.
            var databaseName = Guid.NewGuid().ToString();
            
            services.AddDbContext<TeocuitlaDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: databaseName));

            _serviceProvider = services.BuildServiceProvider();
            
            // Obtener e inicializar el DbContext para la fase de preparación (Arrange)
            _dbContext = _serviceProvider.GetRequiredService<TeocuitlaDbContext>();
            
            var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
            _loggerMock = new Mock<ILogger<ProxyService>>();
            
            _proxyService = new ProxyService(scopeFactory, _loggerMock.Object);
        }

        [Fact]
        public async Task GetNextProxyAsync_WithNoActiveProxies_ReturnsNull()
        {
            // Act
            var result = await _proxyService.GetNextProxyAsync();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetNextProxyAsync_WithActiveProxies_RotatesRoundRobinAndUpdatesLastUsed()
        {
            // Arrange
            var proxy1 = new RegistroProxy { Id = 1, Ip = "192.168.1.1", Puerto = 8080, Activo = true, Baneado = false };
            var proxy2 = new RegistroProxy { Id = 2, Ip = "192.168.1.2", Puerto = 8080, Activo = true, Baneado = false };
            var proxy3 = new RegistroProxy { Id = 3, Ip = "192.168.1.3", Puerto = 8080, Activo = false, Baneado = false }; // Inactivo
            var proxy4 = new RegistroProxy { Id = 4, Ip = "192.168.1.4", Puerto = 8080, Activo = true, Baneado = true };   // Baneado

            await _dbContext.RegistroProxies.AddRangeAsync(proxy1, proxy2, proxy3, proxy4);
            await _dbContext.SaveChangesAsync();

            // Act & Assert
            
            // Primera rotación: debe obtener el proxy 1
            var first = await _proxyService.GetNextProxyAsync();
            Assert.NotNull(first);
            Assert.Equal(1, first.Id);
            Assert.NotNull(first.UltimoUso);
            Assert.True((DateTime.UtcNow - first.UltimoUso.Value).TotalSeconds < 5);

            // Segunda rotación: debe obtener el proxy 2
            var second = await _proxyService.GetNextProxyAsync();
            Assert.NotNull(second);
            Assert.Equal(2, second.Id);

            // Tercera rotación: vuelve al proxy 1 (solo hay 2 activos y no baneados)
            var third = await _proxyService.GetNextProxyAsync();
            Assert.NotNull(third);
            Assert.Equal(1, third.Id);
        }

        [Fact]
        public async Task ReportProxyFailureAsync_IncrementsFailuresAndBansAfter5Failures()
        {
            // Arrange
            var proxy = new RegistroProxy { Id = 1, Ip = "192.168.1.1", Puerto = 8080, Activo = true, Baneado = false, FallosAcumulados = 4 };
            await _dbContext.RegistroProxies.AddAsync(proxy);
            await _dbContext.SaveChangesAsync();

            // Act - Reporta el quinto fallo
            await _proxyService.ReportProxyFailureAsync(1);

            // Limpiar el change tracker para forzar la lectura fresca desde la base de datos en memoria
            _dbContext.ChangeTracker.Clear();

            // Assert
            var updatedProxy = await _dbContext.RegistroProxies.FindAsync(1);
            Assert.NotNull(updatedProxy);
            Assert.Equal(5, updatedProxy.FallosAcumulados);
            Assert.True(updatedProxy.Baneado);
            
            // Verificar que se registró una advertencia en el logger
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ha sido baneado automáticamente")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ReportProxySuccessAsync_ResetsFailuresAndUpdatesLatency()
        {
            // Arrange
            var proxy = new RegistroProxy { Id = 1, Ip = "192.168.1.1", Puerto = 8080, Activo = true, Baneado = false, FallosAcumulados = 3, LatenciaMs = 450 };
            await _dbContext.RegistroProxies.AddAsync(proxy);
            await _dbContext.SaveChangesAsync();

            // Act
            await _proxyService.ReportProxySuccessAsync(1, 120);

            // Limpiar el change tracker para forzar la lectura fresca desde la base de datos en memoria
            _dbContext.ChangeTracker.Clear();

            // Assert
            var updatedProxy = await _dbContext.RegistroProxies.FindAsync(1);
            Assert.NotNull(updatedProxy);
            Assert.Equal(0, updatedProxy.FallosAcumulados);
            Assert.Equal(120, updatedProxy.LatenciaMs);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
            _serviceProvider.Dispose();
        }
    }
}
