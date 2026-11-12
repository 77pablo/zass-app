using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class CuposFundadorA50 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "cupos_fundador",
                columns: new[] { "numero", "perfil_prestador_id", "reservado_hasta" },
                values: new object[,]
                {
                    { (short)21, null, null },
                    { (short)22, null, null },
                    { (short)23, null, null },
                    { (short)24, null, null },
                    { (short)25, null, null },
                    { (short)26, null, null },
                    { (short)27, null, null },
                    { (short)28, null, null },
                    { (short)29, null, null },
                    { (short)30, null, null },
                    { (short)31, null, null },
                    { (short)32, null, null },
                    { (short)33, null, null },
                    { (short)34, null, null },
                    { (short)35, null, null },
                    { (short)36, null, null },
                    { (short)37, null, null },
                    { (short)38, null, null },
                    { (short)39, null, null },
                    { (short)40, null, null },
                    { (short)41, null, null },
                    { (short)42, null, null },
                    { (short)43, null, null },
                    { (short)44, null, null },
                    { (short)45, null, null },
                    { (short)46, null, null },
                    { (short)47, null, null },
                    { (short)48, null, null },
                    { (short)49, null, null },
                    { (short)50, null, null }
                });

            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)3,
                column: "descripcion",
                value: "Precio de lanzamiento para las primeras 50 personas: 5990 pesos al mes mientras mantengas tu suscripcion activa.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)21);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)22);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)23);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)24);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)25);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)26);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)27);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)28);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)29);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)30);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)31);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)32);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)33);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)34);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)35);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)36);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)37);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)38);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)39);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)40);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)41);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)42);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)43);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)44);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)45);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)46);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)47);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)48);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)49);

            migrationBuilder.DeleteData(
                table: "cupos_fundador",
                keyColumn: "numero",
                keyValue: (short)50);

            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)3,
                column: "descripcion",
                value: "Precio de lanzamiento para las primeras 20 personas: 5990 pesos al mes mientras mantengas tu suscripcion activa.");
        }
    }
}
