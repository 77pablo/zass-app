using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class EsquemaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "auditoria",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entidad = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contexto = table.Column<string>(type: "jsonb", nullable: true),
                    ip_origen = table.Column<string>(type: "text", nullable: true),
                    ocurrido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_auditoria", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bloqueos_agenda",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desde_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    hasta_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_bloqueos_agenda", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entidades_certificadoras",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    url_registro_publico = table.Column<string>(type: "text", nullable: true),
                    catalogada = table.Column<bool>(type: "boolean", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_entidades_certificadoras", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planes",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    precio_mensual = table.Column<int>(type: "integer", nullable: false),
                    dias_prueba = table.Column<short>(type: "smallint", nullable: false),
                    max_servicios = table.Column<int>(type: "integer", nullable: false),
                    max_tarjetas = table.Column<int>(type: "integer", nullable: false),
                    permite_credenciales = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_planes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    es_interno = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rubros",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    icono = table.Column<string>(type: "text", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_rubros", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correo = table.Column<string>(type: "citext", nullable: false),
                    hash_contrasena = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    correo_verificado = table.Column<bool>(type: "boolean", nullable: false),
                    token_verificacion = table.Column<string>(type: "text", nullable: true),
                    token_recuperacion = table.Column<string>(type: "text", nullable: true),
                    token_expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    anonimizado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_usuarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "zonas",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    sector = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    centro_latitud = table.Column<double>(type: "double precision", nullable: false),
                    centro_longitud = table.Column<double>(type: "double precision", nullable: false),
                    comuna = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_zonas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "servicios",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rubro_id = table.Column<short>(type: "smallint", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    es_catalogo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_servicios", x => x.id);
                    table.ForeignKey(
                        name: "f_k_servicios_rubros_rubro_id",
                        column: x => x.rubro_id,
                        principalTable: "rubros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notificaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    destinatario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cita_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    clave_idempotencia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    asunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cuerpo = table.Column<string>(type: "text", nullable: false),
                    programada_para = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    enviada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    leida_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    intentos = table.Column<short>(type: "smallint", nullable: false),
                    ultimo_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_notificaciones", x => x.id);
                    table.ForeignKey(
                        name: "f_k_notificaciones_usuarios_destinatario_id",
                        column: x => x.destinatario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "perfiles_prestador",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rubro_id = table.Column<short>(type: "smallint", nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    nombre_publico = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    url_foto = table.Column<string>(type: "text", nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    direccion_cifrada = table.Column<byte[]>(type: "bytea", nullable: true),
                    latitud = table.Column<double>(type: "double precision", nullable: true),
                    longitud = table.Column<double>(type: "double precision", nullable: true),
                    origen_ubicacion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    telefono_publico = table.Column<string>(type: "text", nullable: true),
                    correo_publico = table.Column<string>(type: "text", nullable: true),
                    whatsapp = table.Column<string>(type: "text", nullable: true),
                    sitio_web = table.Column<string>(type: "text", nullable: true),
                    promedio_calificacion = table.Column<decimal>(type: "numeric(2,1)", precision: 2, scale: 1, nullable: true),
                    total_resenas = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    publicado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    actualizado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ubicacion_exacta = table.Column<string>(type: "geography(Point,4326)", nullable: true, computedColumnSql: "CASE WHEN longitud IS NULL OR latitud IS NULL THEN NULL ELSE ST_SetSRID(ST_MakePoint(longitud, latitud), 4326)::geography END", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_perfiles_prestador", x => x.id);
                    table.ForeignKey(
                        name: "f_k_perfiles_prestador_rubros_rubro_id",
                        column: x => x.rubro_id,
                        principalTable: "rubros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_perfiles_prestador_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sesiones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash_token = table.Column<string>(type: "text", nullable: false),
                    ip_origen = table.Column<string>(type: "text", nullable: true),
                    agente_usuario = table.Column<string>(type: "text", nullable: true),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revocada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sesiones", x => x.id);
                    table.ForeignKey(
                        name: "f_k_sesiones_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_roles",
                columns: table => new
                {
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_id = table.Column<short>(type: "smallint", nullable: false),
                    asignado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    asignado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_usuario_roles", x => new { x.usuario_id, x.rol_id });
                    table.ForeignKey(
                        name: "f_k_usuario_roles_roles_rol_id",
                        column: x => x.rol_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_usuario_roles_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "perfiles_buscador",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zona_interes_id = table.Column<int>(type: "integer", nullable: true),
                    radio_preferido_km = table.Column<short>(type: "smallint", nullable: false),
                    acepta_avisos = table.Column<bool>(type: "boolean", nullable: false),
                    canal_correo = table.Column<bool>(type: "boolean", nullable: false),
                    canal_push = table.Column<bool>(type: "boolean", nullable: false),
                    favoritos = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_perfiles_buscador", x => x.id);
                    table.ForeignKey(
                        name: "f_k_perfiles_buscador_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_perfiles_buscador_zonas_zona_interes_id",
                        column: x => x.zona_interes_id,
                        principalTable: "zonas",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "coberturas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zona_id = table.Column<int>(type: "integer", nullable: false),
                    es_principal = table.Column<bool>(type: "boolean", nullable: false),
                    desde_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_coberturas", x => x.id);
                    table.ForeignKey(
                        name: "f_k_coberturas_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_coberturas_zonas_zona_id",
                        column: x => x.zona_id,
                        principalTable: "zonas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credenciales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entidad_certificadora_id = table.Column<short>(type: "smallint", nullable: true),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    obtenida_en = table.Column<DateOnly>(type: "date", nullable: false),
                    vence_en = table.Column<DateOnly>(type: "date", nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sello_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sello_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reservada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    reservada_hasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    esperando_prestador = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_credenciales", x => x.id);
                    table.CheckConstraint("ck_credencial_fechas", "vence_en IS NULL OR vence_en > obtenida_en");
                    table.CheckConstraint("ck_credencial_sello", "estado <> 'Verificada' OR sello_por IS NOT NULL");
                    table.ForeignKey(
                        name: "f_k_credenciales_entidades_certificadoras_entidad_certificadora_~",
                        column: x => x.entidad_certificadora_id,
                        principalTable: "entidades_certificadoras",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "f_k_credenciales_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prestador_servicios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    servicio_id = table.Column<int>(type: "integer", nullable: false),
                    duracion_minutos = table.Column<short>(type: "smallint", nullable: false),
                    modo_agenda = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    precio_referencia = table.Column<int>(type: "integer", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_prestador_servicios", x => x.id);
                    table.ForeignKey(
                        name: "f_k_prestador_servicios_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_prestador_servicios_servicios_servicio_id",
                        column: x => x.servicio_id,
                        principalTable: "servicios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "suscripciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    inicia_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    vigente_hasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cancelada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    renovacion_automatica = table.Column<bool>(type: "boolean", nullable: false),
                    referencia_pasarela = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_suscripciones", x => x.id);
                    table.ForeignKey(
                        name: "f_k_suscripciones_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_suscripciones_planes_plan_id",
                        column: x => x.plan_id,
                        principalTable: "planes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tarjetas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    url_estable = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hash_codigo_activacion = table.Column<string>(type: "text", nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tarjeta_sucesora_id = table.Column<Guid>(type: "uuid", nullable: true),
                    emitida_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    codigo_vence_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    desactivada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    intentos_fallidos = table.Column<short>(type: "smallint", nullable: false),
                    bloqueada_hasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_tarjetas", x => x.id);
                    table.CheckConstraint("ck_tarjeta_dueno", "estado <> 'Activa' OR perfil_prestador_id IS NOT NULL");
                    table.ForeignKey(
                        name: "f_k_tarjetas_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "documentos_credencial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credencial_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave_almacenamiento = table.Column<string>(type: "text", nullable: false),
                    nombre_original = table.Column<string>(type: "text", nullable: false),
                    tipo_mime = table.Column<string>(type: "text", nullable: false),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    subido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_documentos_credencial", x => x.id);
                    table.ForeignKey(
                        name: "f_k_documentos_credencial_credenciales_credencial_id",
                        column: x => x.credencial_id,
                        principalTable: "credenciales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verificaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credencial_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verificador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resultado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    motivo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    detalle = table.Column<string>(type: "text", nullable: true),
                    vigencia_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    resuelta_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_verificaciones", x => x.id);
                    table.CheckConstraint("ck_verificacion_motivo", "resultado <> 'Rechazada' OR motivo IS NOT NULL");
                    table.ForeignKey(
                        name: "f_k_verificaciones_credenciales_credencial_id",
                        column: x => x.credencial_id,
                        principalTable: "credenciales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bloques",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    disponibilidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prestador_servicio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inicio_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fin_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_bloques", x => x.id);
                    table.CheckConstraint("ck_bloque_rango", "fin_en > inicio_en");
                    table.ForeignKey(
                        name: "f_k_bloques_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_bloques_prestador_servicios_prestador_servicio_id",
                        column: x => x.prestador_servicio_id,
                        principalTable: "prestador_servicios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "disponibilidades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prestador_servicio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dia_semana = table.Column<short>(type: "smallint", nullable: false),
                    hora_inicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    hora_fin = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    duracion_bloque_min = table.Column<short>(type: "smallint", nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_disponibilidades", x => x.id);
                    table.ForeignKey(
                        name: "f_k_disponibilidades_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_disponibilidades_prestador_servicios_prestador_servicio_id",
                        column: x => x.prestador_servicio_id,
                        principalTable: "prestador_servicios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pagos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    suscripcion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monto = table.Column<int>(type: "integer", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    periodo_desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    periodo_hasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    referencia_pasarela = table.Column<string>(type: "text", nullable: true),
                    motivo_fallo = table.Column<string>(type: "text", nullable: true),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    pagado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_pagos", x => x.id);
                    table.ForeignKey(
                        name: "f_k_pagos_suscripciones_suscripcion_id",
                        column: x => x.suscripcion_id,
                        principalTable: "suscripciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "escaneos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tarjeta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medio = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ocurrido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    derivo_en_contacto = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_escaneos", x => x.id);
                    table.ForeignKey(
                        name: "f_k_escaneos_tarjetas_tarjeta_id",
                        column: x => x.tarjeta_id,
                        principalTable: "tarjetas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "citas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    bloque_horario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buscador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prestador_servicio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cita_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    modo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nota_buscador = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    clave_idempotencia = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_citas", x => x.id);
                    table.CheckConstraint("ck_cita_origen", "cita_origen_id IS NULL OR cita_origen_id <> id");
                    table.ForeignKey(
                        name: "f_k_citas_bloques_bloque_horario_id",
                        column: x => x.bloque_horario_id,
                        principalTable: "bloques",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_citas_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_citas_prestador_servicios_prestador_servicio_id",
                        column: x => x.prestador_servicio_id,
                        principalTable: "prestador_servicios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_citas_usuarios_buscador_id",
                        column: x => x.buscador_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cambios_cita",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cita_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    estado_anterior = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    estado_nuevo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    realizado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<string>(type: "text", nullable: true),
                    es_tardio = table.Column<bool>(type: "boolean", nullable: false),
                    ocurrido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_cambios_cita", x => x.id);
                    table.ForeignKey(
                        name: "f_k_cambios_cita_citas_cita_id",
                        column: x => x.cita_id,
                        principalTable: "citas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contactos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    buscador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cita_id = table.Column<Guid>(type: "uuid", nullable: true),
                    escaneo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ocurrido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_contactos", x => x.id);
                    table.ForeignKey(
                        name: "f_k_contactos_citas_cita_id",
                        column: x => x.cita_id,
                        principalTable: "citas",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "f_k_contactos_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_contactos_usuarios_buscador_id",
                        column: x => x.buscador_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resenas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contacto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    autor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calificacion = table.Column<short>(type: "smallint", nullable: false),
                    comentario = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    aspectos = table.Column<List<string>>(type: "text[]", nullable: false),
                    editada = table.Column<bool>(type: "boolean", nullable: false),
                    publicada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    editable_hasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_resenas", x => x.id);
                    table.CheckConstraint("ck_resena_calificacion", "calificacion BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_resena_comentario", "char_length(comentario) BETWEEN 20 AND 1500");
                    table.ForeignKey(
                        name: "f_k_resenas_contactos_contacto_id",
                        column: x => x.contacto_id,
                        principalTable: "contactos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_resenas_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_resenas_usuarios_autor_id",
                        column: x => x.autor_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reportes_resena",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resena_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reportado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    resuelto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    detalle = table.Column<string>(type: "text", nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    resolucion = table.Column<string>(type: "text", nullable: true),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resuelto_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_reportes_resena", x => x.id);
                    table.CheckConstraint("ck_reporte_moderador", "resuelto_por IS NULL OR resuelto_por <> reportado_por");
                    table.ForeignKey(
                        name: "f_k_reportes_resena_resenas_resena_id",
                        column: x => x.resena_id,
                        principalTable: "resenas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "respuestas_resena",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resena_id = table.Column<Guid>(type: "uuid", nullable: false),
                    texto = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    publicada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    editada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_respuestas_resena", x => x.id);
                    table.ForeignKey(
                        name: "f_k_respuestas_resena_resenas_resena_id",
                        column: x => x.resena_id,
                        principalTable: "resenas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_auditoria_actor_id_ocurrido_en",
                table: "auditoria",
                columns: new[] { "actor_id", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_auditoria_entidad_entidad_id_ocurrido_en",
                table: "auditoria",
                columns: new[] { "entidad", "entidad_id", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_bloqueos_agenda_perfil_prestador_id_desde_en",
                table: "bloqueos_agenda",
                columns: new[] { "perfil_prestador_id", "desde_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_bloques_perfil_prestador_id_inicio_en",
                table: "bloques",
                columns: new[] { "perfil_prestador_id", "inicio_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_bloques_prestador_servicio_id_inicio_en_estado",
                table: "bloques",
                columns: new[] { "prestador_servicio_id", "inicio_en", "estado" });

            migrationBuilder.CreateIndex(
                name: "i_x_cambios_cita_cita_id_ocurrido_en",
                table: "cambios_cita",
                columns: new[] { "cita_id", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_citas_buscador_id_estado",
                table: "citas",
                columns: new[] { "buscador_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "i_x_citas_cita_origen_id",
                table: "citas",
                column: "cita_origen_id");

            migrationBuilder.CreateIndex(
                name: "i_x_citas_clave_idempotencia",
                table: "citas",
                column: "clave_idempotencia",
                unique: true,
                filter: "clave_idempotencia IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_citas_codigo",
                table: "citas",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_citas_perfil_prestador_id_estado",
                table: "citas",
                columns: new[] { "perfil_prestador_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "i_x_citas_prestador_servicio_id",
                table: "citas",
                column: "prestador_servicio_id");

            migrationBuilder.CreateIndex(
                name: "ux_cita_bloque_activo",
                table: "citas",
                column: "bloque_horario_id",
                unique: true,
                filter: "estado IN ('Solicitada','Confirmada')");

            migrationBuilder.CreateIndex(
                name: "i_x_coberturas_perfil_prestador_id_zona_id",
                table: "coberturas",
                columns: new[] { "perfil_prestador_id", "zona_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_coberturas_zona_id_perfil_prestador_id",
                table: "coberturas",
                columns: new[] { "zona_id", "perfil_prestador_id" });

            migrationBuilder.CreateIndex(
                name: "ux_cobertura_principal",
                table: "coberturas",
                column: "perfil_prestador_id",
                unique: true,
                filter: "es_principal");

            migrationBuilder.CreateIndex(
                name: "i_x_contactos_buscador_id_perfil_prestador_id_ocurrido_en",
                table: "contactos",
                columns: new[] { "buscador_id", "perfil_prestador_id", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_contactos_cita_id",
                table: "contactos",
                column: "cita_id");

            migrationBuilder.CreateIndex(
                name: "i_x_contactos_perfil_prestador_id",
                table: "contactos",
                column: "perfil_prestador_id");

            migrationBuilder.CreateIndex(
                name: "i_x_credenciales_entidad_certificadora_id",
                table: "credenciales",
                column: "entidad_certificadora_id");

            migrationBuilder.CreateIndex(
                name: "i_x_credenciales_estado_creada_en",
                table: "credenciales",
                columns: new[] { "estado", "creada_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_credenciales_perfil_prestador_id_estado",
                table: "credenciales",
                columns: new[] { "perfil_prestador_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "i_x_disponibilidades_perfil_prestador_id_dia_semana",
                table: "disponibilidades",
                columns: new[] { "perfil_prestador_id", "dia_semana" });

            migrationBuilder.CreateIndex(
                name: "i_x_disponibilidades_prestador_servicio_id",
                table: "disponibilidades",
                column: "prestador_servicio_id");

            migrationBuilder.CreateIndex(
                name: "i_x_documentos_credencial_clave_almacenamiento",
                table: "documentos_credencial",
                column: "clave_almacenamiento",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_documentos_credencial_credencial_id",
                table: "documentos_credencial",
                column: "credencial_id");

            migrationBuilder.CreateIndex(
                name: "i_x_entidades_certificadoras_nombre",
                table: "entidades_certificadoras",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_escaneos_tarjeta_id_ocurrido_en",
                table: "escaneos",
                columns: new[] { "tarjeta_id", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_notificaciones_clave_idempotencia",
                table: "notificaciones",
                column: "clave_idempotencia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_notificaciones_destinatario_id_enviada_en",
                table: "notificaciones",
                columns: new[] { "destinatario_id", "enviada_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_notificaciones_estado_programada_para",
                table: "notificaciones",
                columns: new[] { "estado", "programada_para" });

            migrationBuilder.CreateIndex(
                name: "i_x_pagos_suscripcion_id_creado_en",
                table: "pagos",
                columns: new[] { "suscripcion_id", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_perfiles_buscador_usuario_id",
                table: "perfiles_buscador",
                column: "usuario_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_perfiles_buscador_zona_interes_id",
                table: "perfiles_buscador",
                column: "zona_interes_id");

            migrationBuilder.CreateIndex(
                name: "i_x_perfiles_prestador_estado_rubro_id",
                table: "perfiles_prestador",
                columns: new[] { "estado", "rubro_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_perfiles_prestador_rubro_id",
                table: "perfiles_prestador",
                column: "rubro_id");

            migrationBuilder.CreateIndex(
                name: "i_x_perfiles_prestador_slug",
                table: "perfiles_prestador",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_perfiles_prestador_usuario_id",
                table: "perfiles_prestador",
                column: "usuario_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_planes_codigo",
                table: "planes",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_prestador_servicios_perfil_prestador_id_servicio_id",
                table: "prestador_servicios",
                columns: new[] { "perfil_prestador_id", "servicio_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_prestador_servicios_servicio_id",
                table: "prestador_servicios",
                column: "servicio_id");

            migrationBuilder.CreateIndex(
                name: "i_x_reportes_resena_estado",
                table: "reportes_resena",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "i_x_reportes_resena_resena_id",
                table: "reportes_resena",
                column: "resena_id");

            migrationBuilder.CreateIndex(
                name: "i_x_resenas_autor_id",
                table: "resenas",
                column: "autor_id");

            migrationBuilder.CreateIndex(
                name: "i_x_resenas_contacto_id",
                table: "resenas",
                column: "contacto_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_resenas_perfil_prestador_id_estado_publicada_en",
                table: "resenas",
                columns: new[] { "perfil_prestador_id", "estado", "publicada_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_respuestas_resena_resena_id",
                table: "respuestas_resena",
                column: "resena_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_roles_codigo",
                table: "roles",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_rubros_codigo",
                table: "rubros",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_servicios_rubro_id",
                table: "servicios",
                column: "rubro_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sesiones_hash_token",
                table: "sesiones",
                column: "hash_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_sesiones_usuario_id",
                table: "sesiones",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "i_x_suscripciones_estado_vigente_hasta",
                table: "suscripciones",
                columns: new[] { "estado", "vigente_hasta" });

            migrationBuilder.CreateIndex(
                name: "i_x_suscripciones_plan_id",
                table: "suscripciones",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ux_suscripcion_vigente",
                table: "suscripciones",
                column: "perfil_prestador_id",
                unique: true,
                filter: "estado IN ('Prueba','Activa')");

            migrationBuilder.CreateIndex(
                name: "i_x_tarjetas_hash_codigo_activacion",
                table: "tarjetas",
                column: "hash_codigo_activacion",
                unique: true,
                filter: "estado = 'Emitida' AND hash_codigo_activacion IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_tarjetas_perfil_prestador_id",
                table: "tarjetas",
                column: "perfil_prestador_id");

            migrationBuilder.CreateIndex(
                name: "i_x_tarjetas_url_estable",
                table: "tarjetas",
                column: "url_estable",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_usuario_roles_rol_id",
                table: "usuario_roles",
                column: "rol_id");

            migrationBuilder.CreateIndex(
                name: "i_x_usuarios_correo",
                table: "usuarios",
                column: "correo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_usuarios_token_recuperacion",
                table: "usuarios",
                column: "token_recuperacion");

            migrationBuilder.CreateIndex(
                name: "i_x_usuarios_token_verificacion",
                table: "usuarios",
                column: "token_verificacion");

            migrationBuilder.CreateIndex(
                name: "i_x_verificaciones_credencial_id",
                table: "verificaciones",
                column: "credencial_id");

            migrationBuilder.CreateIndex(
                name: "i_x_zonas_sector",
                table: "zonas",
                column: "sector");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria");

            migrationBuilder.DropTable(
                name: "bloqueos_agenda");

            migrationBuilder.DropTable(
                name: "cambios_cita");

            migrationBuilder.DropTable(
                name: "coberturas");

            migrationBuilder.DropTable(
                name: "disponibilidades");

            migrationBuilder.DropTable(
                name: "documentos_credencial");

            migrationBuilder.DropTable(
                name: "escaneos");

            migrationBuilder.DropTable(
                name: "notificaciones");

            migrationBuilder.DropTable(
                name: "pagos");

            migrationBuilder.DropTable(
                name: "perfiles_buscador");

            migrationBuilder.DropTable(
                name: "reportes_resena");

            migrationBuilder.DropTable(
                name: "respuestas_resena");

            migrationBuilder.DropTable(
                name: "sesiones");

            migrationBuilder.DropTable(
                name: "usuario_roles");

            migrationBuilder.DropTable(
                name: "verificaciones");

            migrationBuilder.DropTable(
                name: "tarjetas");

            migrationBuilder.DropTable(
                name: "suscripciones");

            migrationBuilder.DropTable(
                name: "zonas");

            migrationBuilder.DropTable(
                name: "resenas");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "credenciales");

            migrationBuilder.DropTable(
                name: "planes");

            migrationBuilder.DropTable(
                name: "contactos");

            migrationBuilder.DropTable(
                name: "entidades_certificadoras");

            migrationBuilder.DropTable(
                name: "citas");

            migrationBuilder.DropTable(
                name: "bloques");

            migrationBuilder.DropTable(
                name: "prestador_servicios");

            migrationBuilder.DropTable(
                name: "perfiles_prestador");

            migrationBuilder.DropTable(
                name: "servicios");

            migrationBuilder.DropTable(
                name: "usuarios");

            migrationBuilder.DropTable(
                name: "rubros");
        }
    }
}
