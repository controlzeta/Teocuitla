using Microsoft.EntityFrameworkCore;
using Teocuitla.Shared.Data;
using Teocuitla.Shared.Helpers;
using Teocuitla.Shared.Models;

namespace Teocuitla.Tests
{
    public class SelectorCandidateRegistrarTests
    {
        [Fact]
        public async Task RegisterAsync_PromotesSelectorOnlyAfterThreeDistinctVariants()
        {
            await using var context = CreateContext();
            const string xpath = "//*[@data-testid='price']";
            context.CatalogoSitios.Add(new CatalogoSitio { Id = 1, Nombre = "Tienda", UrlBase = "https://tienda.example" });
            context.VariantesComerciales.AddRange(
                CreateVariant(1), CreateVariant(2), CreateVariant(3));
            await context.SaveChangesAsync();

            var first = await SelectorCandidateRegistrar.RegisterAsync(context, 1, 1, xpath, 100m, "https://tienda.example/1");
            var second = await SelectorCandidateRegistrar.RegisterAsync(context, 1, 2, xpath, 100m, "https://tienda.example/2");
            var third = await SelectorCandidateRegistrar.RegisterAsync(context, 1, 3, xpath, 100m, "https://tienda.example/3");

            Assert.False(first.Promoted);
            Assert.False(second.Promoted);
            Assert.True(third.Promoted);
            Assert.Equal(SelectorCandidateRegistrar.PromotionThreshold, third.ValidationCount);
            Assert.Equal(xpath, (await context.CatalogoSitios.FindAsync(1))!.SelectorPrecioXPath);
        }

        [Fact]
        public async Task RegisterAsync_DoesNotCountSameVariantTwice()
        {
            await using var context = CreateContext();
            const string xpath = "//*[@data-testid='price']";
            context.CatalogoSitios.Add(new CatalogoSitio { Id = 1, Nombre = "Tienda", UrlBase = "https://tienda.example" });
            context.VariantesComerciales.Add(CreateVariant(1));
            await context.SaveChangesAsync();

            await SelectorCandidateRegistrar.RegisterAsync(context, 1, 1, xpath, 100m, "https://tienda.example/1");
            var repeated = await SelectorCandidateRegistrar.RegisterAsync(context, 1, 1, xpath, 100m, "https://tienda.example/1");

            Assert.False(repeated.Added);
            Assert.False(repeated.Promoted);
            Assert.Equal(1, repeated.ValidationCount);
        }

        private static TeocuitlaDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<TeocuitlaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new TeocuitlaDbContext(options);
        }

        private static VarianteComercial CreateVariant(int id) => new()
        {
            Id = id,
            CatalogoSitioId = 1,
            ProductoMaestroId = id,
            Sku = $"SKU-{id}",
            Nombre = $"Producto {id}",
            UrlProducto = $"https://tienda.example/{id}"
        };
    }
}
