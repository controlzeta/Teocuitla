using Microsoft.EntityFrameworkCore;
using Teocuitla.Shared.Models;

namespace Teocuitla.Shared.Data
{
    public class TeocuitlaDbContext : DbContext
    {
        public TeocuitlaDbContext(DbContextOptions<TeocuitlaDbContext> options)
            : base(options)
        {
        }

        public DbSet<CatalogoSitio> CatalogoSitios { get; set; } = null!;
        public DbSet<ProductoMaestro> ProductosMaestros { get; set; } = null!;
        public DbSet<VarianteComercial> VariantesComerciales { get; set; } = null!;
        public DbSet<HistorialPrecio> HistorialPrecios { get; set; } = null!;
        public DbSet<RegistroProxy> RegistroProxies { get; set; } = null!;
        public DbSet<RegistroFallaScraping> RegistroFallasScraping { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configurar relación ProductoMaestro -> VarianteComercial
            modelBuilder.Entity<VarianteComercial>()
                .HasOne(v => v.ProductoMaestro)
                .WithMany(p => p.Variantes)
                .HasForeignKey(v => v.ProductoMaestroId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configurar relación CatalogoSitio -> VarianteComercial
            modelBuilder.Entity<VarianteComercial>()
                .HasOne(v => v.CatalogoSitio)
                .WithMany()
                .HasForeignKey(v => v.CatalogoSitioId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configurar relación VarianteComercial -> HistorialPrecio
            modelBuilder.Entity<HistorialPrecio>()
                .HasOne(h => h.VarianteComercial)
                .WithMany(v => v.HistorialPrecios)
                .HasForeignKey(h => h.VarianteComercialId)
                .OnDelete(DeleteBehavior.Cascade);

            // Optimización de HistorialPrecio:
            // 1. Desmarcar la clave primaria 'Id' como Clustered (por defecto SQL Server hace PK clustered)
            modelBuilder.Entity<HistorialPrecio>()
                .HasKey(h => h.Id)
                .IsClustered(false);

            // 2. Crear el índice agrupado (Clustered Index) compuesto sobre (VarianteComercialId, FechaCaptura)
            modelBuilder.Entity<HistorialPrecio>()
                .HasIndex(h => new { h.VarianteComercialId, h.FechaCaptura })
                .IsClustered(true)
                .HasDatabaseName("IX_HistorialPrecios_VarianteId_FechaCaptura_Clustered");

            // Opcional: Índice no agrupado en la fecha de captura sola para estadísticas globales rápidas
            modelBuilder.Entity<HistorialPrecio>()
                .HasIndex(h => h.FechaCaptura)
                .IsClustered(false)
                .HasDatabaseName("IX_HistorialPrecios_FechaCaptura");

            // Configurar índice en SKU de Variante para búsquedas rápidas
            modelBuilder.Entity<VarianteComercial>()
                .HasIndex(v => v.Sku)
                .HasDatabaseName("IX_VariantesComerciales_Sku");

            // Configurar relación VarianteComercial -> RegistroFallaScraping (Cascade on Delete)
            modelBuilder.Entity<RegistroFallaScraping>()
                .HasOne(f => f.VarianteComercial)
                .WithMany()
                .HasForeignKey(f => f.VarianteComercialId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
