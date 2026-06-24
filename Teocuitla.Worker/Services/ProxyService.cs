using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Teocuitla.Shared.Models;
using Teocuitla.Shared.Data; // Compartimos el DbContext

namespace Teocuitla.Worker.Services
{
    public class ProxyService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ProxyService> _logger;
        private int _currentIndex = 0;

        public ProxyService(IServiceScopeFactory scopeFactory, ILogger<ProxyService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<RegistroProxy?> GetNextProxyAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TeocuitlaDbContext>();

            var activeProxies = await context.RegistroProxies
                .Where(p => p.Activo && !p.Baneado)
                .ToListAsync();

            if (activeProxies.Count == 0)
            {
                return null;
            }

            // Rotación simple Round-Robin
            var index = _currentIndex % activeProxies.Count;
            _currentIndex++;

            var proxy = activeProxies[index];
            
            // Actualizar último uso
            proxy.UltimoUso = DateTime.UtcNow;
            context.Entry(proxy).State = EntityState.Modified;
            await context.SaveChangesAsync();

            return proxy;
        }

        public async Task ReportProxyFailureAsync(int proxyId)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TeocuitlaDbContext>();

            var proxy = await context.RegistroProxies.FindAsync(proxyId);
            if (proxy != null)
            {
                proxy.FallosAcumulados++;
                if (proxy.FallosAcumulados >= 5)
                {
                    proxy.Baneado = true;
                    _logger.LogWarning("Proxy {Ip}:{Port} ha sido baneado automáticamente tras acumular 5 fallos.", proxy.Ip, proxy.Puerto);
                }
                context.Entry(proxy).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }
        }

        public async Task ReportProxySuccessAsync(int proxyId, int latencyMs)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TeocuitlaDbContext>();

            var proxy = await context.RegistroProxies.FindAsync(proxyId);
            if (proxy != null)
            {
                proxy.FallosAcumulados = 0;
                proxy.LatenciaMs = latencyMs;
                context.Entry(proxy).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }
        }
    }
}
