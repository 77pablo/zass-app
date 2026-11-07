using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class RubroOpcionalEnBorrador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_perfiles_prestador_rubros_rubro_id",
                table: "perfiles_prestador");

            migrationBuilder.AlterColumn<short>(
                name: "rubro_id",
                table: "perfiles_prestador",
                type: "smallint",
                nullable: true,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddForeignKey(
                name: "f_k_perfiles_prestador_rubros_rubro_id",
                table: "perfiles_prestador",
                column: "rubro_id",
                principalTable: "rubros",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_perfiles_prestador_rubros_rubro_id",
                table: "perfiles_prestador");

            migrationBuilder.AlterColumn<short>(
                name: "rubro_id",
                table: "perfiles_prestador",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "f_k_perfiles_prestador_rubros_rubro_id",
                table: "perfiles_prestador",
                column: "rubro_id",
                principalTable: "rubros",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
