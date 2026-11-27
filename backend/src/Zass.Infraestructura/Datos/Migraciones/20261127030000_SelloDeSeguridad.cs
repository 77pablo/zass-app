using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class SelloDeSeguridad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sello_seguridad",
                table: "usuarios",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "00000000000000000000000000000000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sello_seguridad",
                table: "usuarios");
        }
    }
}
