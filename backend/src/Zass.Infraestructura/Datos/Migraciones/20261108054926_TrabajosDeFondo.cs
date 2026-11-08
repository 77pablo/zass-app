using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class TrabajosDeFondo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trabajos_de_fondo",
                columns: table => new
                {
                    nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    proxima_corrida_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reservado_hasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ultima_corrida_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    corridas = table.Column<int>(type: "integer", nullable: false),
                    ultimo_resumen = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_trabajos_de_fondo", x => x.nombre);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trabajos_de_fondo");
        }
    }
}
