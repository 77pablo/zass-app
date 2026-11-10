using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class PlanesDePagoSembrados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "planes",
                columns: new[] { "id", "activo", "codigo", "descripcion", "dias_prueba", "max_servicios", "max_tarjetas", "nombre", "permite_credenciales", "precio_mensual" },
                values: new object[,]
                {
                    { (short)2, true, "mensual", "Publica tu perfil, apareces en las busquedas y recibes solicitudes de hora. 7990 pesos al mes; cancela cuando quieras.", (short)0, 10, 1, "ZASS! Mensual", true, 7990 },
                    { (short)3, true, "fundador", "Precio de lanzamiento para las primeras 20 personas: 5990 pesos al mes mientras mantengas tu suscripcion activa.", (short)0, 10, 1, "ZASS! Fundador", true, 5990 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)2);

            migrationBuilder.DeleteData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)3);
        }
    }
}
