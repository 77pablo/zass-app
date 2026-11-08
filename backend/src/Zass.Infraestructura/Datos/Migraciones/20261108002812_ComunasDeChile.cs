using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class ComunasDeChile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "sector",
                table: "zonas",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AlterColumn<string>(
                name: "comuna",
                table: "zonas",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "padre_id",
                table: "zonas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "region",
                table: "zonas",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tipo",
                table: "zonas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 1,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 2,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 3,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 4,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 5,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 6,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 7,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 8,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 9,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 10,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 11,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 12,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 13,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 14,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 15,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.UpdateData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 16,
                columns: new[] { "padre_id", "region", "tipo" },
                values: new object[] { 377, "La Araucanía", "Barrio" });

            migrationBuilder.InsertData(
                table: "zonas",
                columns: new[] { "id", "centro_latitud", "centro_longitud", "comuna", "nombre", "padre_id", "region", "sector", "tipo" },
                values: new object[,]
                {
                    { 101, -18.478528799999999, -70.321139400000007, "Arica", "Arica", null, "Arica y Parinacota", "Arica y Parinacota", "Comuna" },
                    { 102, -19.1401644, -70.159213199999996, "Camarones", "Camarones", null, "Arica y Parinacota", "Arica y Parinacota", "Comuna" },
                    { 103, -17.5947669, -69.481553599999998, "General Lagos", "General Lagos", null, "Arica y Parinacota", "Arica y Parinacota", "Comuna" },
                    { 104, -18.196382499999999, -69.559224200000003, "Putre", "Putre", null, "Arica y Parinacota", "Arica y Parinacota", "Comuna" },
                    { 105, -20.2700478, -70.1009162, "Alto Hospicio", "Alto Hospicio", null, "Tarapacá", "Tarapacá", "Comuna" },
                    { 106, -19.313081, -69.426696800000002, "Camiña", "Camiña", null, "Tarapacá", "Tarapacá", "Comuna" },
                    { 107, -19.275822000000002, -68.638947099999996, "Colchane", "Colchane", null, "Tarapacá", "Tarapacá", "Comuna" },
                    { 108, -19.995787199999999, -69.772703399999997, "Huara", "Huara", null, "Tarapacá", "Tarapacá", "Comuna" },
                    { 109, -20.214065699999999, -70.152464600000002, "Iquique", "Iquique", null, "Tarapacá", "Tarapacá", "Comuna" },
                    { 110, -20.4911417, -69.329075200000005, "Pica", "Pica", null, "Tarapacá", "Tarapacá", "Comuna" },
                    { 111, -20.259706099999999, -69.786137199999999, "Pozo Almonte", "Pozo Almonte", null, "Tarapacá", "Tarapacá", "Comuna" },
                    { 112, -23.646374099999999, -70.398003299999999, "Antofagasta", "Antofagasta", null, "Antofagasta", "Antofagasta", "Comuna" },
                    { 113, -22.462391700000001, -68.927218100000005, "Calama", "Calama", null, "Antofagasta", "Antofagasta", "Comuna" },
                    { 114, -22.345148500000001, -69.661803500000005, "María Elena", "María Elena", null, "Antofagasta", "Antofagasta", "Comuna" },
                    { 115, -23.100234199999999, -70.448307600000007, "Mejillones", "Mejillones", null, "Antofagasta", "Antofagasta", "Comuna" },
                    { 116, -21.224223500000001, -68.253452199999998, "Ollagüe", "Ollagüe", null, "Antofagasta", "Antofagasta", "Comuna" },
                    { 117, -22.910831999999999, -68.200137600000005, "San Pedro de Atacama", "San Pedro de Atacama", null, "Antofagasta", "Antofagasta", "Comuna" },
                    { 118, -23.333805900000002, -69.843473299999999, "Sierra Gorda", "Sierra Gorda", null, "Antofagasta", "Antofagasta", "Comuna" },
                    { 119, -25.4078293, -70.485842399999996, "Taltal", "Taltal", null, "Antofagasta", "Antofagasta", "Comuna" },
                    { 120, -22.0887189, -70.196065700000005, "Tocopilla", "Tocopilla", null, "Antofagasta", "Antofagasta", "Comuna" },
                    { 121, -28.759671099999998, -70.486110800000006, "Alto del Carmen", "Alto del Carmen", null, "Atacama", "Atacama", "Comuna" },
                    { 122, -27.0675545, -70.8222047, "Caldera", "Caldera", null, "Atacama", "Atacama", "Comuna" },
                    { 123, -26.347933999999999, -70.622361699999999, "Chañaral", "Chañaral", null, "Atacama", "Atacama", "Comuna" },
                    { 124, -27.3664685, -70.332275300000006, "Copiapó", "Copiapó", null, "Atacama", "Atacama", "Comuna" },
                    { 125, -26.391079399999999, -70.045928099999998, "Diego de Almagro", "Diego de Almagro", null, "Atacama", "Atacama", "Comuna" },
                    { 126, -28.508648399999998, -71.0785719, "Freirina", "Freirina", null, "Atacama", "Atacama", "Comuna" },
                    { 127, -28.465124800000002, -71.220794699999999, "Huasco", "Huasco", null, "Atacama", "Atacama", "Comuna" },
                    { 128, -27.4680505, -70.264930699999994, "Tierra Amarilla", "Tierra Amarilla", null, "Atacama", "Atacama", "Comuna" },
                    { 129, -28.5750438, -70.761639799999998, "Vallenar", "Vallenar", null, "Atacama", "Atacama", "Comuna" },
                    { 130, -30.232204800000002, -71.084755599999994, "Andacollo", "Andacollo", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 131, -31.398418499999998, -71.457573800000006, "Canela", "Canela", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 132, -31.178151799999998, -71.0024382, "Combarbalá", "Combarbalá", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 133, -29.953185099999999, -71.337950300000003, "Coquimbo", "Coquimbo", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 134, -31.632693199999999, -71.168303899999998, "Illapel", "Illapel", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 135, -29.511595, -71.201500699999997, "La Higuera", "La Higuera", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 136, -29.902705000000001, -71.251957500000003, "La Serena", "La Serena", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 137, -31.9121725, -71.513865899999999, "Los Vilos", "Los Vilos", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 138, -30.694476900000002, -70.957959299999999, "Monte Patria", "Monte Patria", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 139, -30.603081899999999, -71.202989400000007, "Ovalle", "Ovalle", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 140, -30.029041700000001, -70.516441400000005, "Paihuano", "Paihuano", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 141, -30.8337532, -71.257363699999999, "Punitaqui", "Punitaqui", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 142, -30.4090831, -70.938309099999998, "Río Hurtado", "Río Hurtado", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 143, -31.780467399999999, -70.964989599999996, "Salamanca", "Salamanca", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 144, -30.034048200000001, -70.712670200000005, "Vicuña", "Vicuña", null, "Coquimbo", "Coquimbo", "Comuna" },
                    { 145, -33.369171999999999, -71.668053299999997, "Algarrobo", "Algarrobo", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 146, -32.426590900000001, -71.066329899999999, "Cabildo", "Cabildo", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 147, -32.855268500000001, -70.626040700000004, "Calle Larga", "Calle Larga", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 148, -33.546849100000003, -71.603170800000001, "Cartagena", "Cartagena", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 149, -33.320586400000003, -71.410076200000006, "Casablanca", "Casablanca", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 150, -32.778421399999999, -70.963342800000007, "Catemu", "Catemu", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 151, -32.922003599999996, -71.515956500000001, "Concón", "Concón", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 152, -33.400311000000002, -71.693997999999993, "El Quisco", "El Quisco", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 153, -33.454584699999998, -71.669106999999997, "El Tabo", "El Tabo", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 154, -32.800057799999998, -71.145221000000006, "Hijuelas", "Hijuelas", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 155, -27.148117200000002, -109.42734400000001, "Isla de Pascua", "Isla de Pascua", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 156, -33.6357292, -78.831316400000006, "Juan Fernández", "Juan Fernández", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 157, -32.788981, -71.203546000000003, "La Calera", "La Calera", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 158, -32.8279116, -71.227153400000006, "La Cruz", "La Cruz", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 159, -32.449637500000001, -71.231669499999995, "La Ligua", "La Ligua", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 160, -33.001874100000002, -71.265731500000001, "Limache", "Limache", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 161, -32.842966500000003, -70.952649300000004, "Llay-Llay", "Llay-Llay", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 162, -32.833686700000001, -70.598160899999996, "Los Andes", "Los Andes", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 163, -32.736381000000002, -71.200243799999996, "Nogales", "Nogales", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 164, -32.995618399999998, -71.186211, "Olmué", "Olmué", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 165, -32.770576900000002, -70.836130299999994, "Panquehue", "Panquehue", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 166, -32.50759, -71.445935399999996, "Papudo", "Papudo", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 167, -32.251699000000002, -70.930910299999994, "Petorca", "Petorca", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 168, -32.725985600000001, -71.415006500000004, "Puchuncaví", "Puchuncaví", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 169, -32.6260768, -70.7167642, "Putaendo", "Putaendo", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 170, -32.879997000000003, -71.247355499999998, "Quillota", "Quillota", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 171, -33.049813499999999, -71.441528199999993, "Quilpué", "Quilpué", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 172, -32.784021699999997, -71.528336100000004, "Quintero", "Quintero", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 173, -32.839381899999999, -70.687182699999994, "Rinconada", "Rinconada", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 174, -33.580861499999997, -71.613237699999999, "San Antonio", "San Antonio", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 175, -32.799054400000003, -70.579581099999999, "San Esteban", "San Esteban", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 176, -32.750686999999999, -70.725268799999995, "San Felipe", "San Felipe", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 177, -32.747874899999999, -70.656737500000006, "Santa María", "Santa María", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 178, -33.640594800000002, -71.626095699999993, "Santo Domingo", "Santo Domingo", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 179, -33.0458456, -71.619674900000007, "Valparaíso", "Valparaíso", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 180, -33.044190299999997, -71.372546400000004, "Villa Alemana", "Villa Alemana", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 181, -33.0244535, -71.551763600000001, "Viña del Mar", "Viña del Mar", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 182, -32.5528294, -71.458818100000002, "Zapallar", "Zapallar", null, "Valparaíso", "Valparaíso", "Comuna" },
                    { 183, -34.032058900000003, -71.0991207, "Alhué", "Alhué", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 184, -33.731974100000002, -70.741961900000007, "Buin", "Buin", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 185, -33.630941900000003, -70.759320200000005, "Calera de Tango", "Calera de Tango", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 186, -33.487986800000002, -70.703081499999996, "Cerrillos", "Cerrillos", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 187, -33.429301100000004, -70.7306399, "Cerro Navia", "Cerro Navia", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 188, -33.202472, -70.674914700000002, "Colina", "Colina", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 189, -33.3960309, -70.670838500000002, "Conchalí", "Conchalí", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 190, -33.402228899999997, -71.129373299999997, "Curacaví", "Curacaví", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 191, -33.5561468, -70.665567899999999, "El Bosque", "El Bosque", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 192, -33.678319199999997, -70.978246499999997, "El Monte", "El Monte", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 193, -33.453637499999999, -70.689865400000002, "Estación Central", "Estación Central", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 194, -33.374461500000002, -70.636298800000006, "Huechuraba", "Huechuraba", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 195, -33.422294999999998, -70.655464800000004, "Independencia", "Independencia", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 196, -33.7537387, -70.903932499999996, "Isla de Maipo", "Isla de Maipo", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 197, -33.534626500000002, -70.6644024, "La Cisterna", "La Cisterna", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 198, -33.520418100000001, -70.600617799999995, "La Florida", "La Florida", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 199, -33.543123399999999, -70.633242600000003, "La Granja", "La Granja", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 200, -33.583359399999999, -70.629827000000006, "La Pintana", "La Pintana", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 201, -33.442281999999999, -70.543272900000005, "La Reina", "La Reina", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 202, -33.286421300000001, -70.8729771, "Lampa", "Lampa", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 203, -33.408454800000001, -70.567148900000007, "Las Condes", "Las Condes", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 204, -33.361601200000003, -70.505195700000002, "Lo Barnechea", "Lo Barnechea", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 205, -33.525078200000003, -70.694539000000006, "Lo Espejo", "Lo Espejo", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 206, -33.442627299999998, -70.717821900000004, "Lo Prado", "Lo Prado", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 207, -33.482163499999999, -70.599192900000006, "Macul", "Macul", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 208, -33.509440900000001, -70.756181999999995, "Maipú", "Maipú", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 209, -33.515982299999997, -71.120032399999999, "María Pinto", "María Pinto", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 210, -33.685513399999998, -71.214579599999993, "Melipilla", "Melipilla", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 211, -33.454316400000003, -70.593635800000001, "Ñuñoa", "Ñuñoa", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 212, -33.567305099999999, -70.802046700000005, "Padre Hurtado", "Padre Hurtado", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 213, -33.810110199999997, -70.739023599999996, "Paine", "Paine", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 214, -33.493830000000003, -70.676075499999996, "Pedro Aguirre Cerda", "Pedro Aguirre Cerda", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 215, -33.605863800000002, -70.878530600000005, "Peñaflor", "Peñaflor", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 216, -33.476591800000001, -70.5418308, "Peñalolén", "Peñalolén", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 217, -33.634880799999998, -70.572996099999997, "Pirque", "Pirque", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 218, -33.432183500000001, -70.609528400000002, "Providencia", "Providencia", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 219, -33.436801699999997, -70.751791499999996, "Pudahuel", "Pudahuel", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 220, -33.609527900000003, -70.575473599999995, "Puente Alto", "Puente Alto", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 221, -33.367767000000001, -70.731549000000001, "Quilicura", "Quilicura", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 222, -33.422563099999998, -70.694489200000007, "Quinta Normal", "Quinta Normal", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 223, -33.402074399999997, -70.642891199999994, "Recoleta", "Recoleta", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 224, -33.4042423, -70.704970799999998, "Renca", "Renca", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 225, -33.5922859, -70.704583799999995, "San Bernardo", "San Bernardo", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 226, -33.496584300000002, -70.629063700000003, "San Joaquín", "San Joaquín", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 227, -33.640397499999999, -70.352784499999999, "San José de Maipo", "San José de Maipo", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 228, -33.485974599999999, -70.6495465, "San Miguel", "San Miguel", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 229, -33.894354499999999, -71.456314899999995, "San Pedro", "San Pedro", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 230, -33.542817499999998, -70.643811999999997, "San Ramón", "San Ramón", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 231, -33.437699500000001, -70.651067100000006, "Santiago", "Santiago", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 232, -33.664438799999999, -70.930277799999999, "Talagante", "Talagante", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 233, -33.0852808, -70.929387700000007, "Tiltil", "Tiltil", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 234, -33.387106199999998, -70.576451599999999, "Vitacura", "Vitacura", null, "Metropolitana de Santiago", "Metropolitana de Santiago", "Comuna" },
                    { 235, -34.728585600000002, -71.273858200000006, "Chépica", "Chépica", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 236, -34.708953000000001, -71.040364199999999, "Chimbarongo", "Chimbarongo", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 237, -34.037700000000001, -70.656542200000004, "Codegua", "Codegua", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 238, -34.2699359, -70.951580100000001, "Coinco", "Coinco", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 239, -34.286279399999998, -71.082572900000002, "Coltauco", "Coltauco", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 240, -34.226073999999997, -70.964891600000001, "Doñihue", "Doñihue", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 241, -34.064474300000001, -70.726298299999996, "Graneros", "Graneros", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 242, -34.205285699999997, -71.654603899999998, "La Estrella", "La Estrella", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 243, -34.291730399999999, -71.309843900000004, "Las Cabras", "Las Cabras", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 244, -34.115422899999999, -71.729686400000006, "Litueche", "Litueche", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 245, -34.727836000000003, -71.644083899999998, "Lolol", "Lolol", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 246, -34.182453000000002, -70.6511584, "Machalí", "Machalí", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 247, -34.442140799999997, -70.944065199999997, "Malloa", "Malloa", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 248, -34.400087999999997, -71.618281499999995, "Marchigüe", "Marchigüe", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 249, -33.979903499999999, -70.712173699999994, "Mostazal", "Mostazal", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 250, -34.652742699999997, -71.195381900000001, "Nancagua", "Nancagua", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 251, -33.953604400000003, -71.830577500000004, "Navidad", "Navidad", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 252, -34.209436599999997, -70.817228799999995, "Olivar", "Olivar", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 253, -34.596054199999998, -71.362478199999998, "Palmilla", "Palmilla", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 254, -34.647639499999997, -71.900846299999998, "Paredones", "Paredones", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 255, -34.478027300000001, -71.479320299999998, "Peralillo", "Peralillo", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 256, -34.395916300000003, -71.169426400000006, "Peumo", "Peumo", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 257, -34.358409700000003, -71.282714999999996, "Pichidegua", "Pichidegua", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 258, -34.385169300000001, -72.004660599999994, "Pichilemu", "Pichilemu", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 259, -34.638592699999997, -71.117365100000001, "Placilla", "Placilla", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 260, -34.604814099999999, -71.654680999999997, "Pumanque", "Pumanque", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 261, -34.354584899999999, -70.963880599999996, "Quinta de Tilcoco", "Quinta de Tilcoco", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 262, -34.170243300000003, -70.740718000000001, "Rancagua", "Rancagua", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 263, -34.443543200000001, -70.713452000000004, "Rengo", "Rengo", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 264, -34.2848586, -70.817512800000003, "Requínoa", "Requínoa", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 265, -34.583790999999998, -70.989121999999995, "San Fernando", "San Fernando", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 266, -34.4392675, -71.076790000000003, "San Vicente de Tagua Tagua", "San Vicente de Tagua Tagua", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 267, -34.640324300000003, -71.366129700000002, "Santa Cruz", "Santa Cruz", null, "O'Higgins", "O'Higgins", "Comuna" },
                    { 268, -35.967128600000002, -72.315427, "Cauquenes", "Cauquenes", null, "Maule", "Maule", "Comuna" },
                    { 269, -35.734183600000001, -72.533164200000002, "Chanco", "Chanco", null, "Maule", "Maule", "Comuna" },
                    { 270, -35.696005700000001, -71.406090699999993, "Colbún", "Colbún", null, "Maule", "Maule", "Comuna" },
                    { 271, -35.331830600000004, -72.4118998, "Constitución", "Constitución", null, "Maule", "Maule", "Comuna" },
                    { 272, -35.0937166, -72.018079400000005, "Curepto", "Curepto", null, "Maule", "Maule", "Comuna" },
                    { 273, -34.985368000000001, -71.239370500000007, "Curicó", "Curicó", null, "Maule", "Maule", "Comuna" },
                    { 274, -35.590777199999998, -72.277620499999998, "Empedrado", "Empedrado", null, "Maule", "Maule", "Comuna" },
                    { 275, -34.9774855, -71.801208599999995, "Hualañé", "Hualañé", null, "Maule", "Maule", "Comuna" },
                    { 276, -34.985689200000003, -71.984587300000001, "Licantén", "Licantén", null, "Maule", "Maule", "Comuna" },
                    { 277, -35.845290499999997, -71.597717299999999, "Linares", "Linares", null, "Maule", "Maule", "Comuna" },
                    { 278, -35.965974899999999, -71.684672300000003, "Longaví", "Longaví", null, "Maule", "Maule", "Comuna" },
                    { 279, -35.521346999999999, -71.691889099999997, "Maule", "Maule", null, "Maule", "Maule", "Comuna" },
                    { 280, -35.113970299999998, -71.279979800000007, "Molina", "Molina", null, "Maule", "Maule", "Comuna" },
                    { 281, -36.141429299999999, -71.822222100000005, "Parral", "Parral", null, "Maule", "Maule", "Comuna" },
                    { 282, -35.383663400000003, -71.446982399999996, "Pelarco", "Pelarco", null, "Maule", "Maule", "Comuna" },
                    { 283, -35.845253800000002, -72.636357000000004, "Pelluhue", "Pelluhue", null, "Maule", "Maule", "Comuna" },
                    { 284, -35.396166299999997, -71.799374499999999, "Pencahue", "Pencahue", null, "Maule", "Maule", "Comuna" },
                    { 285, -34.924607100000003, -71.316688400000004, "Rauco", "Rauco", null, "Maule", "Maule", "Comuna" },
                    { 286, -36.055442900000003, -71.765062299999997, "Retiro", "Retiro", null, "Maule", "Maule", "Comuna" },
                    { 287, -35.281633800000002, -71.258674900000003, "Río Claro", "Río Claro", null, "Maule", "Maule", "Comuna" },
                    { 288, -34.959969600000001, -71.125313399999996, "Romeral", "Romeral", null, "Maule", "Maule", "Comuna" },
                    { 289, -34.999036500000003, -71.381711600000003, "Sagrada Familia", "Sagrada Familia", null, "Maule", "Maule", "Comuna" },
                    { 290, -35.537545899999998, -71.485884100000007, "San Clemente", "San Clemente", null, "Maule", "Maule", "Comuna" },
                    { 291, -35.592393299999998, -71.735308000000003, "San Javier", "San Javier", null, "Maule", "Maule", "Comuna" },
                    { 292, -35.305096599999999, -71.515669900000006, "San Rafael", "San Rafael", null, "Maule", "Maule", "Comuna" },
                    { 293, -35.4265343, -71.666032200000004, "Talca", "Talca", null, "Maule", "Maule", "Comuna" },
                    { 294, -34.866795199999999, -71.161220200000002, "Teno", "Teno", null, "Maule", "Maule", "Comuna" },
                    { 295, -34.884016799999998, -71.992370800000003, "Vichuquén", "Vichuquén", null, "Maule", "Maule", "Comuna" },
                    { 296, -35.675203099999997, -71.744670200000002, "Villa Alegre", "Villa Alegre", null, "Maule", "Maule", "Comuna" },
                    { 297, -35.746412800000002, -71.582079899999997, "Yerbas Buenas", "Yerbas Buenas", null, "Maule", "Maule", "Comuna" },
                    { 298, -36.7422507, -72.298721599999993, "Bulnes", "Bulnes", null, "Ñuble", "Ñuble", "Comuna" },
                    { 299, -36.606661600000002, -72.103319400000004, "Chillán", "Chillán", null, "Ñuble", "Ñuble", "Comuna" },
                    { 300, -36.623003199999999, -72.131709299999997, "Chillán Viejo", "Chillán Viejo", null, "Ñuble", "Ñuble", "Comuna" },
                    { 301, -36.131757200000003, -72.791542300000003, "Cobquecura", "Cobquecura", null, "Ñuble", "Ñuble", "Comuna" },
                    { 302, -36.487600399999998, -72.702331599999994, "Coelemu", "Coelemu", null, "Ñuble", "Ñuble", "Comuna" },
                    { 303, -36.628802, -71.831907700000002, "Coihueco", "Coihueco", null, "Ñuble", "Ñuble", "Comuna" },
                    { 304, -36.897855, -72.024614900000003, "El Carmen", "El Carmen", null, "Ñuble", "Ñuble", "Comuna" },
                    { 305, -36.393589599999999, -72.397643200000005, "Ninhue", "Ninhue", null, "Ñuble", "Ñuble", "Comuna" },
                    { 306, -36.282753999999997, -71.814830700000002, "Ñiquén", "Ñiquén", null, "Ñuble", "Ñuble", "Comuna" },
                    { 307, -36.976738099999999, -72.098856299999994, "Pemuco", "Pemuco", null, "Ñuble", "Ñuble", "Comuna" },
                    { 308, -36.703611199999997, -71.892283199999994, "Pinto", "Pinto", null, "Ñuble", "Ñuble", "Comuna" },
                    { 309, -36.5274395, -72.4285292, "Portezuelo", "Portezuelo", null, "Ñuble", "Ñuble", "Comuna" },
                    { 310, -36.7443758, -72.476442700000007, "Quillón", "Quillón", null, "Ñuble", "Ñuble", "Comuna" },
                    { 311, -36.282708499999998, -72.540810100000002, "Quirihue", "Quirihue", null, "Ñuble", "Ñuble", "Comuna" },
                    { 312, -36.605063999999999, -72.534374400000004, "Ránquil", "Ránquil", null, "Ñuble", "Ñuble", "Comuna" },
                    { 313, -36.424819200000002, -71.958120800000003, "San Carlos", "San Carlos", null, "Ñuble", "Ñuble", "Comuna" },
                    { 314, -36.556603000000003, -71.549903599999993, "San Fabián", "San Fabián", null, "Ñuble", "Ñuble", "Comuna" },
                    { 315, -36.7998233, -72.0303507, "San Ignacio", "San Ignacio", null, "Ñuble", "Ñuble", "Comuna" },
                    { 316, -36.503425399999998, -72.212375100000003, "San Nicolás", "San Nicolás", null, "Ñuble", "Ñuble", "Comuna" },
                    { 317, -36.4307029, -72.665818599999994, "Treguaco", "Treguaco", null, "Ñuble", "Ñuble", "Comuna" },
                    { 318, -37.119366499999998, -72.018853100000001, "Yungay", "Yungay", null, "Ñuble", "Ñuble", "Comuna" },
                    { 319, -37.882026699999997, -71.637378100000006, "Alto Biobío", "Alto Biobío", null, "Biobío", "Biobío", "Comuna" },
                    { 320, -37.329956099999997, -71.679157700000005, "Antuco", "Antuco", null, "Biobío", "Biobío", "Comuna" },
                    { 321, -37.246248000000001, -73.317639900000003, "Arauco", "Arauco", null, "Biobío", "Biobío", "Comuna" },
                    { 322, -37.035829300000003, -72.402679699999993, "Cabrero", "Cabrero", null, "Biobío", "Biobío", "Comuna" },
                    { 323, -37.800407399999997, -73.3990802, "Cañete", "Cañete", null, "Biobío", "Biobío", "Comuna" },
                    { 324, -36.929247799999999, -73.023711500000005, "Chiguayante", "Chiguayante", null, "Biobío", "Biobío", "Comuna" },
                    { 325, -36.827069799999997, -73.050206399999993, "Concepción", "Concepción", null, "Biobío", "Biobío", "Comuna" },
                    { 326, -38.016024299999998, -73.228603399999997, "Contulmo", "Contulmo", null, "Biobío", "Biobío", "Comuna" },
                    { 327, -37.016471199999998, -73.156195299999993, "Coronel", "Coronel", null, "Biobío", "Biobío", "Comuna" },
                    { 328, -37.4758037, -73.345706899999996, "Curanilahue", "Curanilahue", null, "Biobío", "Biobío", "Comuna" },
                    { 329, -36.825304099999997, -72.661446900000001, "Florida", "Florida", null, "Biobío", "Biobío", "Comuna" },
                    { 330, -36.792760800000003, -73.094341400000005, "Hualpén", "Hualpén", null, "Biobío", "Biobío", "Comuna" },
                    { 331, -36.975936300000001, -72.938387399999996, "Hualqui", "Hualqui", null, "Biobío", "Biobío", "Comuna" },
                    { 332, -37.279510600000002, -72.714921200000006, "Laja", "Laja", null, "Biobío", "Biobío", "Comuna" },
                    { 333, -37.610227899999998, -73.656054999999995, "Lebu", "Lebu", null, "Biobío", "Biobío", "Comuna" },
                    { 334, -37.6271305, -73.461841899999996, "Los Álamos", "Los Álamos", null, "Biobío", "Biobío", "Comuna" },
                    { 335, -37.4707455, -72.351686000000001, "Los Ángeles", "Los Ángeles", null, "Biobío", "Biobío", "Comuna" },
                    { 336, -37.094453299999998, -73.156402400000005, "Lota", "Lota", null, "Biobío", "Biobío", "Comuna" },
                    { 337, -37.720087300000003, -72.244131800000005, "Mulchén", "Mulchén", null, "Biobío", "Biobío", "Comuna" },
                    { 338, -37.501829299999997, -72.673109999999994, "Nacimiento", "Nacimiento", null, "Biobío", "Biobío", "Comuna" },
                    { 339, -37.585667800000003, -72.529264299999994, "Negrete", "Negrete", null, "Biobío", "Biobío", "Comuna" },
                    { 340, -36.738583800000001, -72.993774900000005, "Penco", "Penco", null, "Biobío", "Biobío", "Comuna" },
                    { 341, -37.684127099999998, -72.005489299999994, "Quilaco", "Quilaco", null, "Biobío", "Biobío", "Comuna" },
                    { 342, -37.470973399999998, -71.980309300000002, "Quilleco", "Quilleco", null, "Biobío", "Biobío", "Comuna" },
                    { 343, -36.841418300000001, -73.103990899999999, "San Pedro de la Paz", "San Pedro de la Paz", null, "Biobío", "Biobío", "Comuna" },
                    { 344, -37.263083700000003, -72.724027199999995, "San Rosendo", "San Rosendo", null, "Biobío", "Biobío", "Comuna" },
                    { 345, -37.668081299999997, -72.021407999999994, "Santa Bárbara", "Santa Bárbara", null, "Biobío", "Biobío", "Comuna" },
                    { 346, -37.173779400000001, -72.942589999999996, "Santa Juana", "Santa Juana", null, "Biobío", "Biobío", "Comuna" },
                    { 347, -36.714486299999997, -73.114108700000003, "Talcahuano", "Talcahuano", null, "Biobío", "Biobío", "Comuna" },
                    { 348, -38.343866200000001, -73.4934808, "Tirúa", "Tirúa", null, "Biobío", "Biobío", "Comuna" },
                    { 349, -36.617080100000003, -72.957504299999997, "Tomé", "Tomé", null, "Biobío", "Biobío", "Comuna" },
                    { 350, -37.240185699999998, -71.941868099999994, "Tucapel", "Tucapel", null, "Biobío", "Biobío", "Comuna" },
                    { 351, -37.097867600000001, -72.562280099999995, "Yumbel", "Yumbel", null, "Biobío", "Biobío", "Comuna" },
                    { 352, -37.798786999999997, -72.708609699999997, "Angol", "Angol", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 353, -38.7115844, -73.165169899999995, "Carahue", "Carahue", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 354, -38.602559200000002, -72.847662600000007, "Cholchol", "Cholchol", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 355, -37.956631199999997, -72.437437500000001, "Collipulli", "Collipulli", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 356, -38.932972900000003, -72.032089400000004, "Cunco", "Cunco", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 357, -38.437298499999997, -71.8883419, "Curacautín", "Curacautín", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 358, -39.358980000000003, -71.587359199999995, "Curarrehue", "Curarrehue", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 359, -38.060905499999997, -72.375468600000005, "Ercilla", "Ercilla", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 360, -38.951424199999998, -72.625433299999997, "Freire", "Freire", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 361, -38.411123400000001, -72.781250600000007, "Galvarino", "Galvarino", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 362, -39.1031567, -72.675935199999998, "Gorbea", "Gorbea", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 363, -38.534312, -72.435050399999994, "Lautaro", "Lautaro", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 364, -39.370615999999998, -72.630070399999994, "Loncoche", "Loncoche", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 365, -38.454797499999998, -71.370616600000005, "Lonquimay", "Lonquimay", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 366, -37.982956199999997, -72.829182900000006, "Los Sauces", "Los Sauces", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 367, -38.165324099999999, -72.906397499999997, "Lumaco", "Lumaco", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 368, -38.853238099999999, -71.695013900000006, "Melipeuco", "Melipeuco", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 369, -38.745310099999998, -72.951998399999994, "Nueva Imperial", "Nueva Imperial", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 370, -38.767424499999997, -72.5956312, "Padre Las Casas", "Padre Las Casas", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 371, -38.421249299999999, -72.377834399999998, "Perquenco", "Perquenco", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 372, -38.986288399999999, -72.637249999999995, "Pitrufquén", "Pitrufquén", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 373, -39.273117300000003, -71.977760500000002, "Pucón", "Pucón", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 374, -38.033283599999997, -73.072593800000007, "Purén", "Purén", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 375, -37.671709, -72.583264299999996, "Renaico", "Renaico", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 376, -38.792316599999999, -73.396865099999999, "Saavedra", "Saavedra", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 377, -38.7358908, -72.590537999999995, "Temuco", "Temuco", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 378, -38.996745400000002, -73.089184399999994, "Teodoro Schmidt", "Teodoro Schmidt", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 379, -39.178205599999998, -73.163884999999993, "Toltén", "Toltén", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 380, -38.250816, -72.666879800000004, "Traiguén", "Traiguén", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 381, -38.2339494, -72.331656800000005, "Victoria", "Victoria", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 382, -38.670395599999999, -72.224080599999994, "Vilcún", "Vilcún", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 383, -39.278091099999997, -72.227436400000002, "Villarrica", "Villarrica", null, "La Araucanía", "La Araucanía", "Comuna" },
                    { 384, -39.8877399, -73.431454299999999, "Corral", "Corral", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 385, -40.131196000000003, -72.382660200000004, "Futrono", "Futrono", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 386, -40.295239199999997, -73.082036599999995, "La Unión", "La Unión", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 387, -40.3216708, -72.481350199999994, "Lago Ranco", "Lago Ranco", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 388, -39.452222499999998, -72.775422199999994, "Lanco", "Lanco", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 389, -39.863400900000002, -72.812965500000004, "Los Lagos", "Los Lagos", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 390, -39.665979200000002, -72.952127599999997, "Máfil", "Máfil", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 391, -39.539452099999998, -72.961084999999997, "Mariquina", "Mariquina", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 392, -40.0706633, -72.872669500000001, "Paillaco", "Paillaco", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 393, -39.641990900000003, -72.333380099999999, "Panguipulli", "Panguipulli", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 394, -40.333667800000001, -72.956761700000001, "Río Bueno", "Río Bueno", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 395, -39.814126199999997, -73.245985899999994, "Valdivia", "Valdivia", null, "Los Ríos", "Los Ríos", "Comuna" },
                    { 396, -41.868216199999999, -73.828722499999998, "Ancud", "Ancud", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 397, -41.771214299999997, -73.127537899999993, "Calbuco", "Calbuco", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 398, -42.482374999999998, -73.764291799999995, "Castro", "Castro", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 399, -42.9165335, -72.708419199999994, "Chaitén", "Chaitén", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 400, -42.623975600000001, -73.772441599999993, "Chonchi", "Chonchi", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 401, -41.664282399999998, -72.296217400000003, "Cochamó", "Cochamó", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 402, -42.439661100000002, -73.602841499999997, "Curaco de Vélez", "Curaco de Vélez", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 403, -42.379553299999998, -73.647299799999999, "Dalcahue", "Dalcahue", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 404, -41.153140499999999, -73.422314799999995, "Fresia", "Fresia", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 405, -41.125846199999998, -73.060473799999997, "Frutillar", "Frutillar", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 406, -43.185771000000003, -71.866702000000004, "Futaleufú", "Futaleufú", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 407, -41.9660516, -72.470679500000003, "Hualaihué", "Hualaihué", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 408, -41.257570399999999, -73.004742800000002, "Llanquihue", "Llanquihue", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 409, -41.396056799999997, -73.461717699999994, "Los Muermos", "Los Muermos", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 410, -41.616013899999999, -73.595071099999998, "Maullín", "Maullín", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 411, -40.573695499999999, -73.135809100000003, "Osorno", "Osorno", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 412, -43.617826800000003, -71.803958699999995, "Palena", "Palena", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 413, -41.471798, -72.939591500000006, "Puerto Montt", "Puerto Montt", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 414, -40.972760000000001, -72.884217100000001, "Puerto Octay", "Puerto Octay", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 415, -41.317802, -72.982907299999994, "Puerto Varas", "Puerto Varas", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 416, -42.599258499999998, -73.676271400000005, "Puqueldón", "Puqueldón", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 417, -40.912809600000003, -73.158188800000005, "Purranque", "Purranque", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 418, -40.683557100000002, -72.603998799999999, "Puyehue", "Puyehue", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 419, -42.889858199999999, -73.472061999999994, "Queilen", "Queilen", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 420, -43.120030499999999, -73.620302499999994, "Quellón", "Quellón", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 421, -42.144530699999997, -73.473559300000005, "Quemchi", "Quemchi", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 422, -42.471001000000001, -73.488078000000002, "Quinchao", "Quinchao", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 423, -40.796171299999997, -73.216390399999995, "Río Negro", "Río Negro", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 424, -40.604727400000002, -73.470530999999994, "San Juan de la Costa", "San Juan de la Costa", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 425, -40.412760599999999, -73.011577700000004, "San Pablo", "San Pablo", null, "Los Lagos", "Los Lagos", "Comuna" },
                    { 426, -45.4068167, -72.697678699999997, "Aysén", "Aysén", null, "Aysén", "Aysén", "Comuna" },
                    { 427, -46.539662499999999, -71.724581599999993, "Chile Chico", "Chile Chico", null, "Aysén", "Aysén", "Comuna" },
                    { 428, -44.727256699999998, -72.680463000000003, "Cisnes", "Cisnes", null, "Aysén", "Aysén", "Comuna" },
                    { 429, -47.2541656, -72.573212799999993, "Cochrane", "Cochrane", null, "Aysén", "Aysén", "Comuna" },
                    { 430, -45.571180400000003, -72.068486300000004, "Coyhaique", "Coyhaique", null, "Aysén", "Aysén", "Comuna" },
                    { 431, -43.897517299999997, -73.746399699999998, "Guaitecas", "Guaitecas", null, "Aysén", "Aysén", "Comuna" },
                    { 432, -44.240214700000003, -71.849277900000004, "Lago Verde", "Lago Verde", null, "Aysén", "Aysén", "Comuna" },
                    { 433, -48.468480200000002, -72.559249300000005, "O’Higgins", "O’Higgins", null, "Aysén", "Aysén", "Comuna" },
                    { 434, -46.291902899999997, -71.937491600000001, "Río Ibáñez", "Río Ibáñez", null, "Aysén", "Aysén", "Comuna" },
                    { 435, -47.803675400000003, -73.537464400000005, "Tortel", "Tortel", null, "Aysén", "Aysén", "Comuna" },
                    { 436, -62.200425899999999, -58.965369000000003, "Antártica", "Antártica", null, "Magallanes y la Antártica Chilena", "Magallanes y la Antártica Chilena", "Comuna" },
                    { 437, -54.935774899999998, -67.606250399999993, "Cabo de Hornos", "Cabo de Hornos", null, "Magallanes y la Antártica Chilena", "Magallanes y la Antártica Chilena", "Comuna" },
                    { 438, -52.427038199999998, -71.414160499999994, "Laguna Blanca", "Laguna Blanca", null, "Magallanes y la Antártica Chilena", "Magallanes y la Antártica Chilena", "Comuna" },
                    { 439, -51.726200300000002, -72.505998199999993, "Natales", "Natales", null, "Magallanes y la Antártica Chilena", "Magallanes y la Antártica Chilena", "Comuna" },
                    { 440, -53.295673800000003, -70.368740099999997, "Porvenir", "Porvenir", null, "Magallanes y la Antártica Chilena", "Magallanes y la Antártica Chilena", "Comuna" },
                    { 441, -52.776461500000003, -69.290908200000004, "Primavera", "Primavera", null, "Magallanes y la Antártica Chilena", "Magallanes y la Antártica Chilena", "Comuna" },
                    { 442, -53.162568800000003, -70.907821999999996, "Punta Arenas", "Punta Arenas", null, "Magallanes y la Antártica Chilena", "Magallanes y la Antártica Chilena", "Comuna" },
                    { 443, -52.650692599999999, -71.463420799999994, "Río Verde", "Río Verde", null, "Magallanes y la Antártica Chilena", "Magallanes y la Antártica Chilena", "Comuna" },
                    { 444, -52.315863399999998, -69.690567799999997, "San Gregorio", "San Gregorio", null, "Magallanes y la Antártica Chilena", "Magallanes y la Antártica Chilena", "Comuna" },
                    { 445, -53.639723699999998, -69.646328999999994, "Timaukel", "Timaukel", null, "Magallanes y la Antártica Chilena", "Magallanes y la Antártica Chilena", "Comuna" },
                    { 446, -51.2562493, -72.344688500000004, "Torres del Paine", "Torres del Paine", null, "Magallanes y la Antártica Chilena", "Magallanes y la Antártica Chilena", "Comuna" }
                });

            migrationBuilder.CreateIndex(
                name: "i_x_zonas_padre_id",
                table: "zonas",
                column: "padre_id");

            migrationBuilder.CreateIndex(
                name: "i_x_zonas_region",
                table: "zonas",
                column: "region");

            migrationBuilder.AddCheckConstraint(
                name: "ck_zona_padre",
                table: "zonas",
                sql: "(tipo = 'Comuna' AND padre_id IS NULL) OR (tipo = 'Barrio' AND padre_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_zona_tipo",
                table: "zonas",
                sql: "tipo IN ('Comuna', 'Barrio')");

            migrationBuilder.AddForeignKey(
                name: "f_k_zonas_zonas_padre_id",
                table: "zonas",
                column: "padre_id",
                principalTable: "zonas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Dos funciones que usan las busquedas. Viven en la base y no copiadas dentro de cada consulta, por la
            // misma razon que tramo_de_distancia: la regla es una sola.
            //
            // etiqueta_de_zona: como se dice en voz alta. «Centro» solo no dice de que ciudad (todas tienen uno), asi
            // que un barrio se nombra con su comuna. Igual que Zona.Etiqueta() en C#.
            migrationBuilder.Sql(@"
                CREATE FUNCTION etiqueta_de_zona(nombre text, comuna text, tipo text)
                RETURNS text
                LANGUAGE sql IMMUTABLE PARALLEL SAFE
                AS $$
                    SELECT CASE WHEN tipo = 'Barrio' THEN nombre || ', ' || comuna ELSE nombre END
                $$;");

            // zonas_relacionadas: con que zonas cuenta quien busca en una. La propia, sus barrios (si es una comuna)
            // y su comuna (si es un barrio): quien cubre «Temuco» atiende en «Centro, Temuco», y quien cubre solo
            // «Centro, Temuco» aparece al buscar en «Temuco». La usan las tres busquedas y el conteo de la portada
            // (este ultimo en LINQ, con una prueba que compara los dos).
            migrationBuilder.Sql(@"
                CREATE FUNCTION zonas_relacionadas(zona integer)
                RETURNS TABLE(id integer)
                LANGUAGE sql STABLE PARALLEL SAFE
                AS $$
                    SELECT z.id
                    FROM zonas z
                    WHERE z.id = zona
                       OR z.padre_id = zona
                       OR z.id = (SELECT q.padre_id FROM zonas q WHERE q.id = zona)
                $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS zonas_relacionadas(integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS etiqueta_de_zona(text, text, text);");

            migrationBuilder.DropForeignKey(
                name: "f_k_zonas_zonas_padre_id",
                table: "zonas");

            migrationBuilder.DropIndex(
                name: "i_x_zonas_padre_id",
                table: "zonas");

            migrationBuilder.DropIndex(
                name: "i_x_zonas_region",
                table: "zonas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_zona_padre",
                table: "zonas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_zona_tipo",
                table: "zonas");

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 101);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 102);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 103);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 104);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 105);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 106);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 107);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 108);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 109);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 110);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 111);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 112);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 113);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 114);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 115);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 116);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 117);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 118);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 119);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 120);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 121);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 122);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 123);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 124);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 125);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 126);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 127);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 128);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 129);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 130);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 131);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 132);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 133);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 134);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 135);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 136);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 137);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 138);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 139);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 140);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 141);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 142);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 143);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 144);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 145);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 146);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 147);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 148);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 149);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 150);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 151);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 152);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 153);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 154);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 155);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 156);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 157);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 158);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 159);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 160);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 161);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 162);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 163);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 164);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 165);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 166);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 167);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 168);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 169);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 170);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 171);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 172);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 173);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 174);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 175);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 176);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 177);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 178);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 179);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 180);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 181);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 182);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 183);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 184);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 185);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 186);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 187);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 188);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 189);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 190);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 191);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 192);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 193);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 194);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 195);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 196);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 197);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 198);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 199);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 200);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 201);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 202);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 203);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 204);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 205);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 206);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 207);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 208);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 209);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 210);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 211);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 212);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 213);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 214);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 215);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 216);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 217);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 218);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 219);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 220);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 221);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 222);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 223);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 224);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 225);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 226);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 227);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 228);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 229);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 230);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 231);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 232);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 233);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 234);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 235);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 236);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 237);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 238);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 239);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 240);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 241);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 242);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 243);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 244);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 245);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 246);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 247);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 248);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 249);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 250);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 251);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 252);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 253);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 254);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 255);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 256);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 257);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 258);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 259);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 260);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 261);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 262);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 263);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 264);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 265);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 266);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 267);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 268);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 269);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 270);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 271);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 272);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 273);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 274);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 275);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 276);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 277);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 278);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 279);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 280);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 281);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 282);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 283);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 284);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 285);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 286);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 287);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 288);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 289);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 290);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 291);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 292);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 293);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 294);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 295);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 296);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 297);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 298);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 299);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 300);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 301);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 302);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 303);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 304);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 305);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 306);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 307);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 308);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 309);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 310);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 311);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 312);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 313);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 314);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 315);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 316);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 317);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 318);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 319);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 320);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 321);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 322);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 323);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 324);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 325);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 326);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 327);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 328);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 329);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 330);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 331);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 332);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 333);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 334);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 335);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 336);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 337);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 338);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 339);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 340);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 341);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 342);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 343);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 344);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 345);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 346);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 347);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 348);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 349);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 350);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 351);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 352);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 353);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 354);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 355);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 356);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 357);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 358);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 359);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 360);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 361);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 362);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 363);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 364);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 365);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 366);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 367);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 368);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 369);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 370);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 371);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 372);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 373);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 374);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 375);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 376);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 377);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 378);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 379);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 380);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 381);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 382);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 383);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 384);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 385);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 386);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 387);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 388);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 389);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 390);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 391);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 392);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 393);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 394);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 395);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 396);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 397);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 398);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 399);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 400);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 401);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 402);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 403);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 404);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 405);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 406);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 407);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 408);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 409);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 410);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 411);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 412);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 413);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 414);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 415);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 416);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 417);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 418);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 419);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 420);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 421);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 422);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 423);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 424);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 425);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 426);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 427);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 428);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 429);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 430);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 431);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 432);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 433);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 434);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 435);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 436);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 437);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 438);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 439);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 440);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 441);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 442);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 443);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 444);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 445);

            migrationBuilder.DeleteData(
                table: "zonas",
                keyColumn: "id",
                keyValue: 446);

            migrationBuilder.DropColumn(
                name: "padre_id",
                table: "zonas");

            migrationBuilder.DropColumn(
                name: "region",
                table: "zonas");

            migrationBuilder.DropColumn(
                name: "tipo",
                table: "zonas");

            migrationBuilder.AlterColumn<string>(
                name: "sector",
                table: "zonas",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "comuna",
                table: "zonas",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);
        }
    }
}
