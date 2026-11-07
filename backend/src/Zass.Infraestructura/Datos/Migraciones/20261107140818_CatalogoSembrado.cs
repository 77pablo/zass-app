using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class CatalogoSembrado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "nombre_normalizado",
                table: "servicios",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.InsertData(
                table: "rubros",
                columns: new[] { "id", "activo", "codigo", "icono", "nombre" },
                values: new object[,]
                {
                    { (short)1, true, "corretaje", "llave", "Corretaje de propiedades" },
                    { (short)2, true, "derecho", "balanza", "Derecho" },
                    { (short)3, true, "contabilidad", "calculadora", "Contabilidad" },
                    { (short)4, true, "arquitectura", "regla", "Arquitectura y construcción" },
                    { (short)5, true, "oficios", "herramientas", "Técnicos y oficios" }
                });

            migrationBuilder.InsertData(
                table: "servicios",
                columns: new[] { "id", "descripcion", "es_catalogo", "nombre", "nombre_normalizado", "rubro_id" },
                values: new object[,]
                {
                    { 101, null, true, "Tasación comercial", "tasacion comercial", (short)1 },
                    { 102, null, true, "Administración de arriendos", "administracion de arriendos", (short)1 },
                    { 103, null, true, "Venta de casa o departamento", "venta de casa o departamento", (short)1 },
                    { 104, null, true, "Arriendo de propiedad", "arriendo de propiedad", (short)1 },
                    { 105, null, true, "Corretaje de terrenos y parcelas", "corretaje de terrenos y parcelas", (short)1 },
                    { 106, null, true, "Estudio de títulos", "estudio de titulos", (short)1 },
                    { 107, null, true, "Gestión de crédito hipotecario", "gestion de credito hipotecario", (short)1 },
                    { 201, null, true, "Escritura de compraventa", "escritura de compraventa", (short)2 },
                    { 202, null, true, "Contrato de arriendo", "contrato de arriendo", (short)2 },
                    { 203, null, true, "Constitución de sociedad", "constitucion de sociedad", (short)2 },
                    { 204, null, true, "Divorcio de mutuo acuerdo", "divorcio de mutuo acuerdo", (short)2 },
                    { 205, null, true, "Posesión efectiva", "posesion efectiva", (short)2 },
                    { 206, null, true, "Defensa laboral", "defensa laboral", (short)2 },
                    { 207, null, true, "Cobranza de deudas", "cobranza de deudas", (short)2 },
                    { 208, null, true, "Revisión de contratos", "revision de contratos", (short)2 },
                    { 301, null, true, "Declaración de renta", "declaracion de renta", (short)3 },
                    { 302, null, true, "Declaración mensual de IVA", "declaracion mensual de iva", (short)3 },
                    { 303, null, true, "Contabilidad mensual de empresa", "contabilidad mensual de empresa", (short)3 },
                    { 304, null, true, "Liquidación de sueldos", "liquidacion de sueldos", (short)3 },
                    { 305, null, true, "Inicio de actividades en el SII", "inicio de actividades en el sii", (short)3 },
                    { 306, null, true, "Facturación electrónica", "facturacion electronica", (short)3 },
                    { 307, null, true, "Finiquitos y trámites laborales", "finiquitos y tramites laborales", (short)3 },
                    { 401, null, true, "Anteproyecto de vivienda", "anteproyecto de vivienda", (short)4 },
                    { 402, null, true, "Permiso de edificación", "permiso de edificacion", (short)4 },
                    { 403, null, true, "Regularización de ampliaciones", "regularizacion de ampliaciones", (short)4 },
                    { 404, null, true, "Diseño de remodelación", "diseno de remodelacion", (short)4 },
                    { 405, null, true, "Inspección técnica de obra", "inspeccion tecnica de obra", (short)4 },
                    { 406, null, true, "Cálculo estructural", "calculo estructural", (short)4 },
                    { 407, null, true, "Certificado de recepción final", "certificado de recepcion final", (short)4 },
                    { 501, null, true, "Instalación eléctrica", "instalacion electrica", (short)5 },
                    { 502, null, true, "Reparación de gasfitería", "reparacion de gasfiteria", (short)5 },
                    { 503, null, true, "Instalación de calefón", "instalacion de calefon", (short)5 },
                    { 504, null, true, "Pintura de interiores", "pintura de interiores", (short)5 },
                    { 505, null, true, "Instalación de cerámica y piso flotante", "instalacion de ceramica y piso flotante", (short)5 },
                    { 506, null, true, "Reparación de techumbre", "reparacion de techumbre", (short)5 },
                    { 507, null, true, "Cerrajería", "cerrajeria", (short)5 },
                    { 508, null, true, "Instalación de aire acondicionado", "instalacion de aire acondicionado", (short)5 },
                    { 509, null, true, "Soldadura y estructuras metálicas", "soldadura y estructuras metalicas", (short)5 },
                    { 510, null, true, "Mantención de estufas y calefacción", "mantencion de estufas y calefaccion", (short)5 }
                });

            // Escrito a mano: la siembra entro con identificadores explicitos, y eso no
            // mueve nada de lo que la base decide sola.
            //
            // `servicios.id` es GENERATED BY DEFAULT AS IDENTITY, asi que la secuencia
            // sigue en 1 despues de insertar las 39 filas: el primer servicio que
            // agregue un prestador por API-20 pediria el id 1 y chocaria con la clave
            // primaria. Se reinicia por encima del maximo sembrado.
            migrationBuilder.Sql(
                "ALTER TABLE servicios ALTER COLUMN id RESTART WITH 1000;");

            // Por si la tabla ya tenia filas de antes de que existiera la columna: sin
            // esto, dos de ellas con nombre_normalizado = '' romperian el indice unico
            // que viene justo despues. Aqui lower() basta porque es solo para no dejar
            // la columna vacia; la clave de verdad la calcula Texto.ClaveDeNombre.
            migrationBuilder.Sql(
                "UPDATE servicios SET nombre_normalizado = lower(nombre) "
                + "WHERE nombre_normalizado = '';");

            migrationBuilder.CreateIndex(
                name: "ux_servicio_rubro_nombre",
                table: "servicios",
                columns: new[] { "rubro_id", "nombre_normalizado" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_prestador_servicio_duracion",
                table: "prestador_servicios",
                sql: "duracion_minutos >= 10 AND duracion_minutos <= 480");

            migrationBuilder.AddCheckConstraint(
                name: "ck_prestador_servicio_precio",
                table: "prestador_servicios",
                sql: "precio_referencia IS NULL OR precio_referencia >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_servicio_rubro_nombre",
                table: "servicios");

            migrationBuilder.DropCheckConstraint(
                name: "ck_prestador_servicio_duracion",
                table: "prestador_servicios");

            migrationBuilder.DropCheckConstraint(
                name: "ck_prestador_servicio_precio",
                table: "prestador_servicios");

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 101);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 102);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 103);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 104);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 105);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 106);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 107);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 201);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 202);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 203);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 204);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 205);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 206);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 207);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 208);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 301);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 302);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 303);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 304);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 305);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 306);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 307);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 401);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 402);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 403);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 404);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 405);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 406);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 407);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 501);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 502);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 503);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 504);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 505);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 506);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 507);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 508);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 509);

            migrationBuilder.DeleteData(
                table: "servicios",
                keyColumn: "id",
                keyValue: 510);

            migrationBuilder.DeleteData(
                table: "rubros",
                keyColumn: "id",
                keyValue: (short)1);

            migrationBuilder.DeleteData(
                table: "rubros",
                keyColumn: "id",
                keyValue: (short)2);

            migrationBuilder.DeleteData(
                table: "rubros",
                keyColumn: "id",
                keyValue: (short)3);

            migrationBuilder.DeleteData(
                table: "rubros",
                keyColumn: "id",
                keyValue: (short)4);

            migrationBuilder.DeleteData(
                table: "rubros",
                keyColumn: "id",
                keyValue: (short)5);

            migrationBuilder.DropColumn(
                name: "nombre_normalizado",
                table: "servicios");
        }
    }
}
