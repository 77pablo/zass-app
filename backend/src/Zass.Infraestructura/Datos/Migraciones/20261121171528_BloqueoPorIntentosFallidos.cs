using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class BloqueoPorIntentosFallidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "bloqueada_hasta",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "intentos_fallidos",
                table: "usuarios",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bloqueada_hasta",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "intentos_fallidos",
                table: "usuarios");
        }
    }
}
