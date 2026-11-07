using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class PlanDePruebaSembrado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "planes",
                columns: new[] { "id", "activo", "codigo", "descripcion", "dias_prueba", "max_servicios", "max_tarjetas", "nombre", "permite_credenciales", "precio_mensual" },
                values: new object[] { (short)1, true, "prueba", "30 dias para publicar el perfil y aparecer en las busquedas.", (short)30, 10, 1, "Prueba", true, 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "planes",
                keyColumn: "id",
                keyValue: (short)1);
        }
    }
}
