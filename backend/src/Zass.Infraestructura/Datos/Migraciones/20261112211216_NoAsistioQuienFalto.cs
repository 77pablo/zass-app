using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class NoAsistioQuienFalto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quien_falto",
                table: "citas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Nadie");

            migrationBuilder.CreateIndex(
                name: "ix_cita_en_disputa",
                table: "citas",
                column: "quien_falto",
                filter: "quien_falto = 'Discrepancia'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cita_quien_falto",
                table: "citas",
                sql: "quien_falto = 'Nadie' OR estado = 'NoAsistio'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cita_en_disputa",
                table: "citas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cita_quien_falto",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "quien_falto",
                table: "citas");
        }
    }
}
