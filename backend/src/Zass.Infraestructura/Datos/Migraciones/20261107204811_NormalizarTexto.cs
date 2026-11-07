using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class NormalizarTexto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La barra de busqueda compara texto SIN tildes ni mayusculas: quien escribe "garcia" tiene
            // que encontrar a "García", y "tecnico" a "Técnicos y oficios". Esta funcion hace en la base
            // lo mismo que TextoDeBusqueda.Normalizar hace en C#, y una prueba de integracion las compara.
            //
            // translate y no la extension unaccent: unaccent hay que instalarla en el servidor, y la
            // base portatil de desarrollo y la de produccion no tienen por que traerla. Cubre las
            // letras del espanol, que son las que aparecen en los nombres de Temuco.
            //
            // Las MAYUSCULAS con tilde van en el translate y no se confian a lower(): lower() depende de
            // la configuracion regional de la base, y en una con locale «C» no pasa «Ñ» ni «Í» a
            // minuscula, asi que «GARCÍA» no coincidia con «garcia». Lo cazo la prueba que compara
            // esta funcion con la de C#.
            migrationBuilder.Sql(@"
                CREATE FUNCTION normalizar_texto(texto text)
                RETURNS text
                LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
                AS $$
                    SELECT translate(lower(texto), 'áéíóúüñàèìòùÁÉÍÓÚÜÑÀÈÌÒÙ', 'aeiouunaeiouaeiouunaeiou')
                $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS normalizar_texto(text);");
        }
    }
}
