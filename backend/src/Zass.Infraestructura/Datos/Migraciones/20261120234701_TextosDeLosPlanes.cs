using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class TextosDeLosPlanes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)1,
                column: "descripcion",
                value: "30 días para publicar tu perfil y aparecer en las búsquedas.");

            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)2,
                column: "descripcion",
                value: "Publica tu perfil, aparece en las búsquedas y recibe solicitudes de hora. Cancela cuando quieras.");

            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)3,
                column: "descripcion",
                value: "Precio de lanzamiento para las primeras 50 personas, congelado mientras mantengas tu suscripción activa.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)1,
                column: "descripcion",
                value: "30 dias para publicar el perfil y aparecer en las busquedas.");

            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)2,
                column: "descripcion",
                value: "Publica tu perfil, apareces en las busquedas y recibes solicitudes de hora. 7990 pesos al mes; cancela cuando quieras.");

            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)3,
                column: "descripcion",
                value: "Precio de lanzamiento para las primeras 50 personas: 5990 pesos al mes mientras mantengas tu suscripcion activa.");
        }
    }
}
