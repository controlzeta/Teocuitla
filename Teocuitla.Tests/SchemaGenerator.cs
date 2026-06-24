using System;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Teocuitla.Shared.Data;

namespace Teocuitla.Tests
{
    public class SchemaGenerator
    {
        [Fact]
        public void GenerateSqliteDb()
        {
            var dbPath = @"d:\Github\Teocuitla\DBSchema.db";
            
            // Eliminar si ya existe para asegurar un esquema limpio
            if (System.IO.File.Exists(dbPath))
            {
                System.IO.File.Delete(dbPath);
            }

            var optionsBuilder = new DbContextOptionsBuilder<TeocuitlaDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");

            using var context = new TeocuitlaDbContext(optionsBuilder.Options);
            
            // EnsureCreated creará la base de datos SQLite con todas las tablas, columnas, índices y relaciones físicas
            context.Database.EnsureCreated();

            Assert.True(System.IO.File.Exists(dbPath), "El archivo de base de datos SQLite no fue creado.");
            Console.WriteLine($"Base de datos SQLite generada correctamente en: {dbPath}");
        }
    }
}
