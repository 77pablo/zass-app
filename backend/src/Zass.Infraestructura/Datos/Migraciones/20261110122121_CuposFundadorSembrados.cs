using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class CuposFundadorSembrados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cupos_fundador",
                columns: table => new
                {
                    numero = table.Column<short>(type: "smallint", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reservado_hasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_cupos_fundador", x => x.numero);
                    table.ForeignKey(
                        name: "f_k_cupos_fundador_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "cupos_fundador",
                columns: new[] { "numero", "perfil_prestador_id", "reservado_hasta" },
                values: new object[,]
                {
                    { (short)1, null, null },
                    { (short)2, null, null },
                    { (short)3, null, null },
                    { (short)4, null, null },
                    { (short)5, null, null },
                    { (short)6, null, null },
                    { (short)7, null, null },
                    { (short)8, null, null },
                    { (short)9, null, null },
                    { (short)10, null, null },
                    { (short)11, null, null },
                    { (short)12, null, null },
                    { (short)13, null, null },
                    { (short)14, null, null },
                    { (short)15, null, null },
                    { (short)16, null, null },
                    { (short)17, null, null },
                    { (short)18, null, null },
                    { (short)19, null, null },
                    { (short)20, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "ux_cupo_fundador_perfil",
                table: "cupos_fundador",
                column: "perfil_prestador_id",
                unique: true,
                filter: "perfil_prestador_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cupos_fundador");
        }
    }
}
