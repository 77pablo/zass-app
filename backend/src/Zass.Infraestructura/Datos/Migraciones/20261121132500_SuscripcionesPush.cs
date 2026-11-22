using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class SuscripcionesPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suscripciones_push",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    clave_p256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    clave_auth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ultimo_uso_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_suscripciones_push", x => x.id);
                    table.ForeignKey(
                        name: "f_k_suscripciones_push_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_suscripciones_push_endpoint",
                table: "suscripciones_push",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_suscripciones_push_usuario_id",
                table: "suscripciones_push",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suscripciones_push");
        }
    }
}
