using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Teocuitla.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddProductImageSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagenUrl",
                table: "Variantes_Comerciales",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectorImagenXPath",
                table: "Catalogo_Sitios",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagenUrl",
                table: "Variantes_Comerciales");

            migrationBuilder.DropColumn(
                name: "SelectorImagenXPath",
                table: "Catalogo_Sitios");
        }
    }
}
