using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class RubrosDeInteres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<short>>(
                name: "rubros_interes",
                table: "perfiles_buscador",
                type: "smallint[]",
                nullable: false,
                defaultValueSql: "'{}'::smallint[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rubros_interes",
                table: "perfiles_buscador");
        }
    }
}
