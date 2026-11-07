using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class RolesDeCatalogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "codigo", "es_interno", "nombre" },
                values: new object[,]
                {
                    { (short)1, "buscador", false, "Buscador" },
                    { (short)2, "prestador", false, "Prestador" },
                    { (short)3, "verificador", true, "Verificador" },
                    { (short)4, "moderador", true, "Moderador" },
                    { (short)5, "administrador", true, "Administrador" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: (short)1);

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: (short)2);

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: (short)3);

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: (short)4);

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: (short)5);
        }
    }
}
