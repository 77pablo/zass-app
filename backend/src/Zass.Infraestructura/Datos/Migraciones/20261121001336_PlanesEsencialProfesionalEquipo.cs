using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class PlanesEsencialProfesionalEquipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_fotos_trabajos",
                table: "planes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "permite_estadisticas",
                table: "planes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "verificacion_prioritaria",
                table: "planes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)1,
                columns: new[] { "max_fotos_trabajos", "permite_estadisticas", "verificacion_prioritaria" },
                values: new object[] { 0, false, false });

            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)2,
                columns: new[] { "descripcion", "max_fotos_trabajos", "max_servicios", "nombre", "permite_estadisticas", "verificacion_prioritaria" },
                values: new object[] { "Para empezar: tu ficha en la búsqueda, agenda en línea, reseñas y sello Verificado. Hasta 5 servicios.", 0, 5, "ZASS! Esencial", false, false });

            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)3,
                columns: new[] { "descripcion", "max_fotos_trabajos", "max_servicios", "max_tarjetas", "permite_estadisticas", "verificacion_prioritaria" },
                values: new object[] { "Todo lo del plan Profesional a precio de lanzamiento, congelado mientras mantengas tu suscripción activa. Solo para las primeras 50 personas.", 12, 15, 3, true, true });

            migrationBuilder.InsertData(
                table: "planes",
                columns: new[] { "id", "activo", "codigo", "descripcion", "dias_prueba", "max_fotos_trabajos", "max_servicios", "max_tarjetas", "nombre", "permite_credenciales", "permite_estadisticas", "precio_mensual", "verificacion_prioritaria" },
                values: new object[,]
                {
                    { (short)4, true, "profesional", "Para quien vive de su trabajo: estadísticas de tu ficha, fotos de tus trabajos, verificación prioritaria, hasta 15 servicios y 3 tarjetas.", (short)0, 12, 15, 3, "ZASS! Profesional", true, true, 12990, true },
                    { (short)5, false, "equipo", "Para estudios, consultas y talleres: hasta 5 profesionales, cada uno con todo lo del Profesional. Próximamente.", (short)0, 12, 15, 10, "ZASS! Equipo", true, true, 24990, true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)4);

            migrationBuilder.DeleteData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)5);

            migrationBuilder.DropColumn(
                name: "max_fotos_trabajos",
                table: "planes");

            migrationBuilder.DropColumn(
                name: "permite_estadisticas",
                table: "planes");

            migrationBuilder.DropColumn(
                name: "verificacion_prioritaria",
                table: "planes");

            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)2,
                columns: new[] { "descripcion", "max_servicios", "nombre" },
                values: new object[] { "Publica tu perfil, aparece en las búsquedas y recibe solicitudes de hora. Cancela cuando quieras.", 10, "ZASS! Mensual" });

            migrationBuilder.UpdateData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)3,
                columns: new[] { "descripcion", "max_servicios", "max_tarjetas" },
                values: new object[] { "Precio de lanzamiento para las primeras 50 personas, congelado mientras mantengas tu suscripción activa.", 10, 1 });
        }
    }
}
