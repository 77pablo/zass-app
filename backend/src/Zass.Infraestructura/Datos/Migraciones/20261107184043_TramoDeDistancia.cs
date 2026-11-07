using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class TramoDeDistancia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // El tramo de distancia: el numero que la busqueda MUESTRA ("a 3 km") y por el
            // que ORDENA. Vive en la base una sola vez, y no copiado dentro del SQL de la
            // consulta, por una razon: la regla ya existe en C# (Geo.DistanciaPublica) y si
            // SQL redondeara distinto, el orden contradiria lo que dice la pantalla. Una
            // prueba de integracion compara esta funcion con la regla del dominio.
            //
            // Por que se ordena por el tramo y no por los metros exactos: dos prestadores
            // a "a 3 km" que cambian de orden segun desde donde se busca delatan en que
            // lado de una linea estan, y con muchas busquedas esas lineas se cruzan.
            // Ordenar por lo que se muestra no filtra nada mas que lo que ya se muestra.
            //
            // numeric y no double: round(double precision) redondea los empates al par en
            // PostgreSQL, y Geo.DistanciaPublica los redondea alejandose de cero. Con
            // numeric los dos coinciden (2,5 km es "a 3 km" en las dos puntas).
            migrationBuilder.Sql(@"
                CREATE FUNCTION tramo_de_distancia(metros double precision)
                RETURNS integer
                LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
                AS $$
                    SELECT CASE WHEN metros < 1000 THEN 0
                                ELSE round((metros / 1000.0)::numeric)::integer END
                $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tramo_de_distancia(double precision);");
        }
    }
}
