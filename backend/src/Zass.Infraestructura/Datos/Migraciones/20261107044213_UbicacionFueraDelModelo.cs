using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <summary>
    /// Saca `ubicacion_exacta` del modelo de EF Core sin sacarla de la base.
    ///
    /// EF Core genero aqui un DropColumn, y se quito a mano: la columna tiene que
    /// seguir existiendo. La crea EsquemaInicial y la mantiene PostgreSQL sola, desde
    /// latitud y longitud. Lo unico que cambia es que EF Core ya no la conoce, y por eso
    /// deja de intentar leerla de vuelta despues de cada INSERT, que es lo que fallaba
    /// (`geography` no se convierte a string).
    ///
    /// Lo que esta migracion SI agrega es el indice GIST, que faltaba. Sin el, la
    /// busqueda por cercania de CU-08 recorreria la tabla entera: la columna estaba
    /// pero no servia para lo unico que justifica tenerla.
    /// </summary>
    public partial class UbicacionFueraDelModelo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_perfiles_prestador_ubicacion "
                + "ON perfiles_prestador USING GIST (ubicacion_exacta);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_perfiles_prestador_ubicacion;");
        }
    }
}
