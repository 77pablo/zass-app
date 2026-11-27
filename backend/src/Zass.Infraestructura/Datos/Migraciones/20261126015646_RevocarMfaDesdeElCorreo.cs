using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class RevocarMfaDesdeElCorreo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "mfa_revocacion_expira_en",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mfa_revocacion_hash",
                table: "usuarios",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "i_x_usuarios_mfa_revocacion_hash",
                table: "usuarios",
                column: "mfa_revocacion_hash",
                filter: "mfa_revocacion_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "i_x_usuarios_mfa_revocacion_hash",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "mfa_revocacion_expira_en",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "mfa_revocacion_hash",
                table: "usuarios");
        }
    }
}
