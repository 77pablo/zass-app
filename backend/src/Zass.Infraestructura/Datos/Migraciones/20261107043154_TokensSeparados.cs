using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class TokensSeparados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "token_expira_en",
                table: "usuarios",
                newName: "token_verificacion_expira_en");

            migrationBuilder.AddColumn<DateTime>(
                name: "token_recuperacion_expira_en",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "token_recuperacion_expira_en",
                table: "usuarios");

            migrationBuilder.RenameColumn(
                name: "token_verificacion_expira_en",
                table: "usuarios",
                newName: "token_expira_en");
        }
    }
}
