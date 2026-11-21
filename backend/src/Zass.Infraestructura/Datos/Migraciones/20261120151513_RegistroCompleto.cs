using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class RegistroCompleto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "apellido",
                table: "usuarios",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "mayor_de_edad_confirmado_en",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "razon_social",
                table: "usuarios",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rut",
                table: "usuarios",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "telefono",
                table: "usuarios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo",
                table: "usuarios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Particular");

            migrationBuilder.CreateIndex(
                name: "i_x_usuarios_rut",
                table: "usuarios",
                column: "rut",
                unique: true,
                filter: "rut IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "i_x_usuarios_rut",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "apellido",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "mayor_de_edad_confirmado_en",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "razon_social",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "rut",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "telefono",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "tipo",
                table: "usuarios");
        }
    }
}
