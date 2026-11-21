using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class FotosDeTrabajos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fotos_de_trabajo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave_almacen = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_fotos_de_trabajo", x => x.id);
                    table.ForeignKey(
                        name: "f_k_fotos_de_trabajo_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_fotos_de_trabajo_perfil_prestador_id_orden",
                table: "fotos_de_trabajo",
                columns: new[] { "perfil_prestador_id", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fotos_de_trabajo");
        }
    }
}
