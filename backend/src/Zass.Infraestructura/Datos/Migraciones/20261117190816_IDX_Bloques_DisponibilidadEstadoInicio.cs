using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class IDX_Bloques_DisponibilidadEstadoInicio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_bloque_disponibilidad_estado_inicio",
                table: "bloques",
                columns: new[] { "disponibilidad_id", "estado", "inicio_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bloque_disponibilidad_estado_inicio",
                table: "bloques");
        }
    }
}
