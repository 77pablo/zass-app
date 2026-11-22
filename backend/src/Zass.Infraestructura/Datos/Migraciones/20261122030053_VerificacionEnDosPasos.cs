using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class VerificacionEnDosPasos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "mfa_activado_en",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "mfa_codigos_respaldo",
                table: "usuarios",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "mfa_desafio_expira_en",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mfa_desafio_hash",
                table: "usuarios",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "mfa_secreto_cifrado",
                table: "usuarios",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "mfa_secreto_pendiente_cifrado",
                table: "usuarios",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "mfa_ultimo_paso",
                table: "usuarios",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "i_x_usuarios_mfa_desafio_hash",
                table: "usuarios",
                column: "mfa_desafio_hash",
                filter: "mfa_desafio_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "i_x_usuarios_mfa_desafio_hash",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "mfa_activado_en",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "mfa_codigos_respaldo",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "mfa_desafio_expira_en",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "mfa_desafio_hash",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "mfa_secreto_cifrado",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "mfa_secreto_pendiente_cifrado",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "mfa_ultimo_paso",
                table: "usuarios");
        }
    }
}
