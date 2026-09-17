using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Teocuitla.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractionQualityMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Registro_Metricas_Extraccion",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CatalogoSitioId = table.Column<int>(type: "int", nullable: false),
                    VarianteComercialId = table.Column<int>(type: "int", nullable: false),
                    Exitoso = table.Column<bool>(type: "bit", nullable: false),
                    MetodoDeteccion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FuentePrecio = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConfianzaPrecio = table.Column<int>(type: "int", nullable: false),
                    LatenciaMs = table.Column<int>(type: "int", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registro_Metricas_Extraccion", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetricasExtraccion_Sitio_Fecha",
                table: "Registro_Metricas_Extraccion",
                columns: new[] { "CatalogoSitioId", "FechaRegistro" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Registro_Metricas_Extraccion");
        }
    }
}
