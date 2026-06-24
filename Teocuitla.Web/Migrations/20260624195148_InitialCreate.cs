using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Teocuitla.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Catalogo_Sitios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UrlBase = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    IntervaloMinutos = table.Column<int>(type: "int", nullable: false),
                    SelectorProductoXPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SelectorPrecioXPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SelectorStockXPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SelectorNombreXPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EstrategiaEvasion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UltimoRastreo = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Catalogo_Sitios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Productos_Maestros",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Marca = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Categoria = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Productos_Maestros", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Registro_Proxies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ip = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Puerto = table.Column<int>(type: "int", nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Password = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    LatenciaMs = table.Column<int>(type: "int", nullable: false),
                    FallosAcumulados = table.Column<int>(type: "int", nullable: false),
                    UltimoUso = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Baneado = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registro_Proxies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Variantes_Comerciales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductoMaestroId = table.Column<int>(type: "int", nullable: false),
                    CatalogoSitioId = table.Column<int>(type: "int", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UrlProducto = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PrecioActual = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PrecioAnterior = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    EnStock = table.Column<bool>(type: "bit", nullable: false),
                    UltimaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Variantes_Comerciales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Variantes_Comerciales_Catalogo_Sitios_CatalogoSitioId",
                        column: x => x.CatalogoSitioId,
                        principalTable: "Catalogo_Sitios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Variantes_Comerciales_Productos_Maestros_ProductoMaestroId",
                        column: x => x.ProductoMaestroId,
                        principalTable: "Productos_Maestros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Historial_Precios",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VarianteComercialId = table.Column<int>(type: "int", nullable: false),
                    Precio = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EnStock = table.Column<bool>(type: "bit", nullable: false),
                    FechaCaptura = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Historial_Precios", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_Historial_Precios_Variantes_Comerciales_VarianteComercialId",
                        column: x => x.VarianteComercialId,
                        principalTable: "Variantes_Comerciales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistorialPrecios_FechaCaptura",
                table: "Historial_Precios",
                column: "FechaCaptura")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.CreateIndex(
                name: "IX_HistorialPrecios_VarianteId_FechaCaptura_Clustered",
                table: "Historial_Precios",
                columns: new[] { "VarianteComercialId", "FechaCaptura" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Variantes_Comerciales_CatalogoSitioId",
                table: "Variantes_Comerciales",
                column: "CatalogoSitioId");

            migrationBuilder.CreateIndex(
                name: "IX_Variantes_Comerciales_ProductoMaestroId",
                table: "Variantes_Comerciales",
                column: "ProductoMaestroId");

            migrationBuilder.CreateIndex(
                name: "IX_VariantesComerciales_Sku",
                table: "Variantes_Comerciales",
                column: "Sku");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Historial_Precios");

            migrationBuilder.DropTable(
                name: "Registro_Proxies");

            migrationBuilder.DropTable(
                name: "Variantes_Comerciales");

            migrationBuilder.DropTable(
                name: "Catalogo_Sitios");

            migrationBuilder.DropTable(
                name: "Productos_Maestros");
        }
    }
}
