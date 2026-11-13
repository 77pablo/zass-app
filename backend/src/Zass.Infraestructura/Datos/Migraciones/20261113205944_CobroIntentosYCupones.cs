using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zass.Infraestructura.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class CobroIntentosYCupones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "referencia_pasarela",
                table: "pagos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "intento_de_pago_id",
                table: "pagos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cupones",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    porcentaje_descuento = table.Column<short>(type: "smallint", nullable: false),
                    vence_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_cupones", x => x.codigo);
                    table.CheckConstraint("ck_cupon_porcentaje", "porcentaje_descuento > 0 AND porcentaje_descuento <= 100");
                });

            migrationBuilder.CreateTable(
                name: "intentos_de_pago",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    suscripcion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<short>(type: "smallint", nullable: false),
                    meses = table.Column<short>(type: "smallint", nullable: false),
                    monto_bruto = table.Column<int>(type: "integer", nullable: false),
                    monto_descuento = table.Column<int>(type: "integer", nullable: false),
                    monto = table.Column<int>(type: "integer", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    codigo_cupon = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    clave_idempotencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    pasarela = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    referencia_pasarela = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    url_para_pagar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resuelto_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo_fallo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_intentos_de_pago", x => x.id);
                    table.CheckConstraint("ck_intento_pago_cupon", "(codigo_cupon IS NULL AND monto_descuento = 0) OR (codigo_cupon IS NOT NULL AND monto_descuento > 0)");
                    table.CheckConstraint("ck_intento_pago_montos", "monto_bruto >= 0 AND monto_descuento >= 0 AND monto_descuento <= monto_bruto AND monto = monto_bruto - monto_descuento");
                    table.ForeignKey(
                        name: "f_k_intentos_de_pago_planes_plan_id",
                        column: x => x.plan_id,
                        principalTable: "planes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_intentos_de_pago_suscripciones_suscripcion_id",
                        column: x => x.suscripcion_id,
                        principalTable: "suscripciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "canjes_de_cupon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_cupon = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    perfil_prestador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pago_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monto_descontado = table.Column<int>(type: "integer", nullable: false),
                    canjeado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_canjes_de_cupon", x => x.id);
                    table.ForeignKey(
                        name: "f_k_canjes_de_cupon_cupones_codigo_cupon",
                        column: x => x.codigo_cupon,
                        principalTable: "cupones",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_canjes_de_cupon_pagos_pago_id",
                        column: x => x.pago_id,
                        principalTable: "pagos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_canjes_de_cupon_perfiles_prestador_perfil_prestador_id",
                        column: x => x.perfil_prestador_id,
                        principalTable: "perfiles_prestador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "cupones",
                columns: new[] { "codigo", "activo", "descripcion", "porcentaje_descuento", "vence_en" },
                values: new object[] { "LANZAMIENTO30", true, "30 % de descuento sobre el primer mes pagado.", (short)30, new DateTime(2027, 3, 31, 23, 59, 59, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "ux_pago_intento",
                table: "pagos",
                column: "intento_de_pago_id",
                unique: true,
                filter: "intento_de_pago_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_canjes_de_cupon_codigo_cupon",
                table: "canjes_de_cupon",
                column: "codigo_cupon");

            migrationBuilder.CreateIndex(
                name: "ux_canje_cupon_pago",
                table: "canjes_de_cupon",
                column: "pago_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_canje_cupon_perfil",
                table: "canjes_de_cupon",
                column: "perfil_prestador_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_intentos_de_pago_perfil_prestador_id_creado_en",
                table: "intentos_de_pago",
                columns: new[] { "perfil_prestador_id", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "i_x_intentos_de_pago_plan_id",
                table: "intentos_de_pago",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "i_x_intentos_de_pago_suscripcion_id",
                table: "intentos_de_pago",
                column: "suscripcion_id");

            migrationBuilder.CreateIndex(
                name: "ux_intento_pago_clave",
                table: "intentos_de_pago",
                column: "clave_idempotencia",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "canjes_de_cupon");

            migrationBuilder.DropTable(
                name: "intentos_de_pago");

            migrationBuilder.DropTable(
                name: "cupones");

            migrationBuilder.DropIndex(
                name: "ux_pago_intento",
                table: "pagos");

            migrationBuilder.DropColumn(
                name: "intento_de_pago_id",
                table: "pagos");

            migrationBuilder.AlterColumn<string>(
                name: "referencia_pasarela",
                table: "pagos",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
