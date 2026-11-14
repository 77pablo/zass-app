using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class IndicesDeClavesForaneas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_suscripcion_perfil_inicia",
                table: "suscripciones",
                columns: new[] { "perfil_prestador_id", "inicia_en" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_cita_bloque_horario",
                table: "citas",
                column: "bloque_horario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_suscripcion_perfil_inicia",
                table: "suscripciones");

            migrationBuilder.DropIndex(
                name: "ix_cita_bloque_horario",
                table: "citas");
        }
    }
}
