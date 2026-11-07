using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class DescripcionMaximaA1500 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Acortar un varchar en PostgreSQL NO trunca en silencio: falla si alguna fila es
            // mas larga, con un error de truncamiento que no dice cual ni que hacer. Se
            // comprueba antes para que, si pasara en una base con datos, el mensaje diga
            // exactamente que revisar. En la base local no hay ninguna (comprobado: 0).
            migrationBuilder.Sql(@"
                DO $$
                DECLARE largas integer;
                BEGIN
                    SELECT count(*) INTO largas FROM perfiles_prestador
                    WHERE length(descripcion) > 1500;
                    IF largas > 0 THEN
                        RAISE EXCEPTION
                          'No se puede bajar el limite de la descripcion a 1500: % perfil(es) tienen una mas larga. Acortarlas o corregirlas antes de migrar.', largas;
                    END IF;
                END $$;");

            migrationBuilder.AlterColumn<string>(
                name: "descripcion",
                table: "perfiles_prestador",
                type: "character varying(1500)",
                maxLength: 1500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "descripcion",
                table: "perfiles_prestador",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1500)",
                oldMaxLength: 1500,
                oldNullable: true);
        }
    }
}
