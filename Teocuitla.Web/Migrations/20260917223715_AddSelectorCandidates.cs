using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Teocuitla.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSelectorCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Variantes_Comerciales_CatalogoSitioId",
                table: "Variantes_Comerciales");

            migrationBuilder.CreateTable(
                name: "Selectores_Candidatos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CatalogoSitioId = table.Column<int>(type: "int", nullable: false),
                    VarianteComercialId = table.Column<int>(type: "int", nullable: false),
                    XPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PrecioVerificado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UrlProducto = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FechaValidacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Selectores_Candidatos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VariantesComerciales_SitioId_Sku",
                table: "Variantes_Comerciales",
                columns: new[] { "CatalogoSitioId", "Sku" });

            migrationBuilder.CreateIndex(
                name: "IX_SelectoresCandidatos_Sitio_XPath",
                table: "Selectores_Candidatos",
                columns: new[] { "CatalogoSitioId", "XPath" });

            migrationBuilder.CreateIndex(
                name: "UX_SelectoresCandidatos_Sitio_XPath_Variante",
                table: "Selectores_Candidatos",
                columns: new[] { "CatalogoSitioId", "XPath", "VarianteComercialId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Selectores_Candidatos");

            migrationBuilder.DropIndex(
                name: "IX_VariantesComerciales_SitioId_Sku",
                table: "Variantes_Comerciales");

            migrationBuilder.CreateIndex(
                name: "IX_Variantes_Comerciales_CatalogoSitioId",
                table: "Variantes_Comerciales",
                column: "CatalogoSitioId");
        }
    }
}
