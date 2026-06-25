using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Teocuitla.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddActivoToVarianteComercial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "Variantes_Comerciales",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Activo",
                table: "Variantes_Comerciales");
        }
    }
}
