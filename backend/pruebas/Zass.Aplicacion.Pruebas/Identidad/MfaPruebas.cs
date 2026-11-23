using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Infraestructura.Seguridad;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>TOTP (RFC 6238) y las reglas de ServicioMfa, sin base de datos.</summary>
public class MfaPruebas
{
    // RFC 6238, apendice B: la clave ASCII "12345678901234567890" en Base32.
    private const string SecretoRfc = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new();
    private readonly HashDeJuguete _hash = new();
    private readonly SecretosDeterministas _secretos = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly CifradoDeJuguete _cifrado = new();
    private readonly ServicioSesion _sesion;
    private readonly ServicioMfa _mfa;
    private readonly ServicioMfa _mfaReal; // con AES-GCM de verdad: lo de los datos asociados se prueba aqui
    private readonly CifradoMfaAesGcm _cifradoReal = new(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private readonly ColaEnMemoria _cola = new();
    private const string Clave = "contrasena-larga-1";

    public MfaPruebas()
    {
        var usuarios = new RepositorioUsuariosEnMemoria(_base);
        _sesion = new ServicioSesion(usuarios, new RepositorioSesionesEnMemoria(_base),
            _base, _hash, _secretos, _reloj, _auditoria);
        _mfa = new ServicioMfa(usuarios, _base, _hash, _secretos, _cifrado, _reloj, _auditoria, _sesion, _cola);
        _mfaReal = new ServicioMfa(usuarios, _base, _hash, _secretos, _cifradoReal, _reloj, _auditoria, _sesion, _cola);
    }

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(20000000000L, "353130")]
    public void Totp_coincide_con_los_vectores_de_la_rfc_6238(long segundos, string esperado) =>
        Assert.Equal(esperado, Totp.Codigo(SecretoRfc, segundos / 30));

    [Fact]
    public void Totp_acepta_un_paso_a_cada_lado_y_no_dos()
    {
        var ahora = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
        var paso = Totp.PasoDe(ahora);
        Assert.Equal(paso - 1, Totp.PasoDelCodigo(SecretoRfc, Totp.Codigo(SecretoRfc, paso - 1), ahora));
        Assert.Equal(paso + 1, Totp.PasoDelCodigo(SecretoRfc, Totp.Codigo(SecretoRfc, paso + 1), ahora));
        Assert.Null(Totp.PasoDelCodigo(SecretoRfc, Totp.Codigo(SecretoRfc, paso + 2), ahora));
        Assert.Null(Totp.PasoDelCodigo(SecretoRfc, "12345", ahora));
    }

    [Fact]
    public void Base32_ida_y_vuelta()
    {
        var bytes = Enumerable.Range(0, 20).Select(i => (byte)(i * 13)).ToArray();
        Assert.Equal(bytes, Totp.DesdeBase32(Totp.Base32(bytes)));
        Assert.Equal(SecretoRfc, Totp.Base32("12345678901234567890"u8));
    }

    private Usuario ConMfa()
    {
        var u = new Usuario
        {
            Correo = "pablo@correo.cl", Nombre = "Pablo", HashContrasena = _hash.Hash(Clave),
            Estado = EstadoCuenta.Activa, CorreoVerificado = true,
            MfaActivadoEn = _reloj.Ahora,
            MfaCodigosRespaldo = Array.Empty<string>(),
        };
        u.MfaSecretoCifrado = _cifrado.Cifrar(SecretoRfc, ServicioMfa.DatosAsociados(u.Id));
        _base.Usuarios.Add(u);
        return u;
    }

    [Fact]
    public async Task Con_mfa_iniciar_no_abre_sesion_y_el_segundo_paso_si()
    {
        ConMfa();
        var r = await _sesion.EntrarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave));
        Assert.True(r.Exito);
        Assert.Null(r.Valor!.Sesion);
        Assert.Empty(_base.Sesiones);

        var codigo = Totp.Codigo(SecretoRfc, Totp.PasoDe(_reloj.Ahora));
        var dos = await _mfa.SegundoPasoAsync(new SegundoPasoCmd(r.Valor.Desafio!.Desafio, codigo));
        Assert.True(dos.Exito, dos.Mensaje);
        Assert.Single(_base.Sesiones);

        // Y el de un paso (IniciarAsync) no deja entrar a una cuenta con MFA.
        Assert.False((await _sesion.IniciarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Exito);
    }

    [Fact]
    public async Task El_mismo_codigo_totp_no_sirve_dos_veces()
    {
        ConMfa();
        var codigo = Totp.Codigo(SecretoRfc, Totp.PasoDe(_reloj.Ahora));
        var d1 = (await _sesion.EntrarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!.Desafio!;
        Assert.True((await _mfa.SegundoPasoAsync(new SegundoPasoCmd(d1.Desafio, codigo))).Exito);

        var d2 = (await _sesion.EntrarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!.Desafio!;
        var r = await _mfa.SegundoPasoAsync(new SegundoPasoCmd(d2.Desafio, codigo));
        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoAutorizado, r.Codigo);
    }

    [Fact]
    public async Task El_desafio_vence_a_los_cinco_minutos()
    {
        ConMfa();
        var d = (await _sesion.EntrarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!.Desafio!;
        _reloj.Avanzar(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var codigo = Totp.Codigo(SecretoRfc, Totp.PasoDe(_reloj.Ahora));
        Assert.False((await _mfa.SegundoPasoAsync(new SegundoPasoCmd(d.Desafio, codigo))).Exito);
    }

    [Fact]
    public async Task Alta_confirmada_entrega_diez_codigos_que_sirven_una_vez()
    {
        var u = ConMfa();
        u.QuitarMfa();

        var alta = await _mfa.IniciarAltaAsync(u.Id);
        Assert.True(alta.Exito);
        Assert.False(u.MfaActivo);
        var codigo = Totp.Codigo(alta.Valor!.Secreto, Totp.PasoDe(_reloj.Ahora));
        var conf = await _mfa.ConfirmarAltaAsync(u.Id, new ConfirmarMfaCmd(Clave, codigo));
        Assert.True(conf.Exito, conf.Mensaje);
        Assert.Equal(10, conf.Valor!.Codigos.Length);
        Assert.True(u.MfaActivo);
        Assert.Contains(_auditoria.Registros, a => a.Accion == "mfa_activado");

        var d = (await _sesion.EntrarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!.Desafio!;
        var rr = await _mfa.SegundoPasoAsync(new SegundoPasoCmd(d.Desafio, conf.Valor.Codigos[0]));
        Assert.True(rr.Exito, rr.Mensaje + " " + string.Join(",", _auditoria.Registros.Select(a => a.Accion + a.Contexto)));
        Assert.Equal(9, u.MfaCodigosRespaldo!.Length);
        Assert.Contains(_auditoria.Registros, a => a.Accion == "mfa_codigo_respaldo_usado");
    }

    [Fact]
    public async Task Al_activar_se_avisa_por_correo_a_la_duena()
    {
        var u = ConMfa();
        u.QuitarMfa();
        var alta = await _mfa.IniciarAltaAsync(u.Id);

        // Un codigo malo no activa ni avisa.
        await _mfa.ConfirmarAltaAsync(u.Id, new ConfirmarMfaCmd(Clave, "000000"));
        Assert.Empty(_cola.Encoladas);

        var codigo = Totp.Codigo(alta.Valor!.Secreto, Totp.PasoDe(_reloj.Ahora));
        Assert.True((await _mfa.ConfirmarAltaAsync(u.Id, new ConfirmarMfaCmd(Clave, codigo))).Exito);

        var aviso = Assert.Single(_cola.Encoladas);
        Assert.Equal(TipoNotificacion.MfaActivado, aviso.Tipo);
        Assert.Equal(CanalNotificacion.Correo, aviso.Canal);
        Assert.Equal(u.Id, aviso.DestinatarioId);
    }

    [Fact]
    public async Task Regenerar_entrega_diez_codigos_nuevos_y_los_viejos_dejan_de_servir()
    {
        var u = ConMfa();
        u.QuitarMfa();
        var alta = await _mfa.IniciarAltaAsync(u.Id);
        var viejos = (await _mfa.ConfirmarAltaAsync(u.Id,
            new ConfirmarMfaCmd(Clave, Totp.Codigo(alta.Valor!.Secreto, Totp.PasoDe(_reloj.Ahora))))).Valor!.Codigos;

        _reloj.Avanzar(TimeSpan.FromSeconds(60));   // el codigo del alta ya se gasto
        var codigo = Totp.Codigo(alta.Valor.Secreto, Totp.PasoDe(_reloj.Ahora));
        var r = await _mfa.RegenerarCodigosAsync(u.Id, new RegenerarCodigosMfaCmd(Clave, codigo));
        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(10, r.Valor!.Codigos.Length);
        Assert.Empty(r.Valor.Codigos.Intersect(viejos));
        Assert.Contains(_auditoria.Registros, a => a.Accion == "mfa_codigos_regenerados");

        var d = (await _sesion.EntrarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!.Desafio!;
        Assert.False((await _mfa.SegundoPasoAsync(new SegundoPasoCmd(d.Desafio, viejos[0]))).Exito);
        var d2 = (await _sesion.EntrarAsync(new IniciarSesionCmd("pablo@correo.cl", Clave))).Valor!.Desafio!;
        Assert.True((await _mfa.SegundoPasoAsync(new SegundoPasoCmd(d2.Desafio, r.Valor.Codigos[0]))).Exito);
    }

    [Fact]
    public async Task Regenerar_pide_contrasena_y_codigo_y_el_fallo_suma_al_bloqueo()
    {
        var u = ConMfa();
        var codigo = Totp.Codigo(SecretoRfc, Totp.PasoDe(_reloj.Ahora));

        var sinClave = await _mfa.RegenerarCodigosAsync(u.Id, new RegenerarCodigosMfaCmd("otra-contrasena-9", codigo));
        Assert.False(sinClave.Exito);
        var sinCodigo = await _mfa.RegenerarCodigosAsync(u.Id, new RegenerarCodigosMfaCmd(Clave, "000000"));
        Assert.False(sinCodigo.Exito);
        Assert.Equal(sinClave.Mensaje, sinCodigo.Mensaje);   // la misma respuesta: no dice cual fallo
        Assert.Equal(2, u.IntentosFallidos);
        Assert.Empty(u.MfaCodigosRespaldo!);                  // no cambio nada
    }

    [Fact]
    public async Task Confirmar_el_alta_con_contrasena_mala_falla_no_activa_y_suma_al_bloqueo()
    {
        var u = ConMfa();
        u.QuitarMfa();
        var alta = await _mfa.IniciarAltaAsync(u.Id);
        var codigo = Totp.Codigo(alta.Valor!.Secreto, Totp.PasoDe(_reloj.Ahora));

        var r = await _mfa.ConfirmarAltaAsync(u.Id, new ConfirmarMfaCmd("otra-contrasena-9", codigo));

        Assert.False(r.Exito);
        Assert.False(u.MfaActivo);
        Assert.Equal(1, u.IntentosFallidos);
        Assert.Empty(_cola.Encoladas);
        Assert.Contains(_auditoria.Registros, a => a.Accion == "mfa_fallido");
    }

    [Fact]
    public async Task Confirmar_el_alta_con_codigo_malo_da_el_mismo_mensaje_que_con_contrasena_mala()
    {
        var u = ConMfa();
        u.QuitarMfa();
        var alta = await _mfa.IniciarAltaAsync(u.Id);
        var codigo = Totp.Codigo(alta.Valor!.Secreto, Totp.PasoDe(_reloj.Ahora));

        var sinClave = await _mfa.ConfirmarAltaAsync(u.Id, new ConfirmarMfaCmd("otra-contrasena-9", codigo));
        var sinCodigo = await _mfa.ConfirmarAltaAsync(u.Id, new ConfirmarMfaCmd(Clave, "000000"));

        Assert.False(sinCodigo.Exito);
        Assert.Equal(sinClave.Mensaje, sinCodigo.Mensaje);
        Assert.Equal(sinClave.Codigo, sinCodigo.Codigo);
        Assert.Equal(2, u.IntentosFallidos);
        Assert.False(u.MfaActivo);
    }

    private static Sesion SesionDe(Usuario u, string hashToken, DateTime ahora) => new()
    {
        UsuarioId = u.Id, HashToken = hashToken, CreadaEn = ahora, ExpiraEn = ahora.AddDays(7),
    };

    [Fact]
    public async Task Activar_cierra_las_otras_sesiones_y_deja_la_actual()
    {
        var u = ConMfa();
        u.QuitarMfa();
        var ahora = _reloj.Ahora;
        var actual = SesionDe(u, _secretos.HashToken("actual"), ahora);
        var otra = SesionDe(u, _secretos.HashToken("otra"), ahora);
        _base.Sesiones.AddRange([actual, otra]);

        var alta = await _mfa.IniciarAltaAsync(u.Id);
        var codigo = Totp.Codigo(alta.Valor!.Secreto, Totp.PasoDe(ahora));
        var r = await _mfa.ConfirmarAltaAsync(u.Id, new ConfirmarMfaCmd(Clave, codigo), sesionActual: "actual");

        Assert.True(r.Exito, r.Mensaje);
        Assert.Null(actual.RevocadaEn);
        Assert.NotNull(otra.RevocadaEn);
    }

    [Fact]
    public async Task Desactivar_cierra_las_otras_sesiones_y_deja_la_actual()
    {
        var u = ConMfa();
        var ahora = _reloj.Ahora;
        var actual = SesionDe(u, _secretos.HashToken("actual"), ahora);
        var otra = SesionDe(u, _secretos.HashToken("otra"), ahora);
        _base.Sesiones.AddRange([actual, otra]);

        var r = await _mfa.DesactivarAsync(u.Id,
            new DesactivarMfaCmd(Clave, Totp.Codigo(SecretoRfc, Totp.PasoDe(ahora))), sesionActual: "actual");

        Assert.True(r.Exito, r.Mensaje);
        Assert.Null(actual.RevocadaEn);
        Assert.NotNull(otra.RevocadaEn);
    }

    [Fact]
    public async Task Regenerar_cierra_las_otras_sesiones_y_deja_la_actual()
    {
        var u = ConMfa();
        var ahora = _reloj.Ahora;
        var actual = SesionDe(u, _secretos.HashToken("actual"), ahora);
        var otra = SesionDe(u, _secretos.HashToken("otra"), ahora);
        _base.Sesiones.AddRange([actual, otra]);

        var r = await _mfa.RegenerarCodigosAsync(u.Id,
            new RegenerarCodigosMfaCmd(Clave, Totp.Codigo(SecretoRfc, Totp.PasoDe(ahora))), sesionActual: "actual");

        Assert.True(r.Exito, r.Mensaje);
        Assert.Null(actual.RevocadaEn);
        Assert.NotNull(otra.RevocadaEn);
    }

    [Fact]
    public async Task Un_fallo_al_desactivar_no_cierra_ninguna_sesion()
    {
        var u = ConMfa();
        var otra = SesionDe(u, _secretos.HashToken("otra"), _reloj.Ahora);
        _base.Sesiones.Add(otra);

        var r = await _mfa.DesactivarAsync(u.Id, new DesactivarMfaCmd("otra-contrasena-9", "000000"), sesionActual: "actual");

        Assert.False(r.Exito);
        Assert.Null(otra.RevocadaEn);
    }

    // ------------------------------------------- Secreto atado a la persona (datos asociados)

    private Usuario Cuenta(string correo)
    {
        var u = new Usuario
        {
            Correo = correo, Nombre = correo, HashContrasena = _hash.Hash(Clave),
            Estado = EstadoCuenta.Activa, CorreoVerificado = true, MfaCodigosRespaldo = Array.Empty<string>(),
        };
        _base.Usuarios.Add(u);
        return u;
    }

    [Fact]
    public async Task El_alta_y_el_segundo_paso_andan_con_el_secreto_atado_a_la_persona()
    {
        var u = Cuenta("ana@correo.cl");
        var alta = await _mfaReal.IniciarAltaAsync(u.Id);
        Assert.True(alta.Exito);
        // Se guardo atado a su id: sin ese dato asociado NO se abre.
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => _cifradoReal.Descifrar(u.MfaSecretoPendienteCifrado!));
        Assert.Equal(alta.Valor!.Secreto,
            _cifradoReal.Descifrar(u.MfaSecretoPendienteCifrado!, ServicioMfa.DatosAsociados(u.Id)));

        var codigo = Totp.Codigo(alta.Valor.Secreto, Totp.PasoDe(_reloj.Ahora));
        Assert.True((await _mfaReal.ConfirmarAltaAsync(u.Id, new ConfirmarMfaCmd(Clave, codigo))).Exito);
        Assert.True(u.MfaActivo);

        _reloj.Avanzar(TimeSpan.FromMinutes(1));
        var r = await _sesion.EntrarAsync(new IniciarSesionCmd(u.Correo, Clave));
        var dos = await _mfaReal.SegundoPasoAsync(new SegundoPasoCmd(
            r.Valor!.Desafio!.Desafio, Totp.Codigo(alta.Valor.Secreto, Totp.PasoDe(_reloj.Ahora))));
        Assert.True(dos.Exito, dos.Mensaje);
        Assert.Single(_base.Sesiones);
    }

    [Fact]
    public async Task El_secreto_pendiente_copiado_a_otra_cuenta_no_deja_confirmar_el_alta()
    {
        var ana = Cuenta("ana@correo.cl");
        var beto = Cuenta("beto@correo.cl");
        var alta = await _mfaReal.IniciarAltaAsync(ana.Id);
        beto.MfaSecretoPendienteCifrado = ana.MfaSecretoPendienteCifrado; // copia de la columna entre filas
        var codigo = Totp.Codigo(alta.Valor!.Secreto, Totp.PasoDe(_reloj.Ahora));

        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(
            () => _mfaReal.ConfirmarAltaAsync(beto.Id, new ConfirmarMfaCmd(Clave, codigo)));

        Assert.False(beto.MfaActivo);
    }

    [Fact]
    public async Task El_secreto_activo_copiado_a_otra_cuenta_no_deja_pasar_el_segundo_paso()
    {
        var ana = Cuenta("ana@correo.cl");
        var beto = Cuenta("beto@correo.cl");
        ana.MfaSecretoCifrado = _cifradoReal.Cifrar(SecretoRfc, ServicioMfa.DatosAsociados(ana.Id));
        ana.MfaActivadoEn = _reloj.Ahora;
        beto.MfaSecretoCifrado = ana.MfaSecretoCifrado; // copia de la columna entre filas
        beto.MfaActivadoEn = _reloj.Ahora;

        var r = await _sesion.EntrarAsync(new IniciarSesionCmd(beto.Correo, Clave));
        var codigo = Totp.Codigo(SecretoRfc, Totp.PasoDe(_reloj.Ahora));

        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(
            () => _mfaReal.SegundoPasoAsync(new SegundoPasoCmd(r.Valor!.Desafio!.Desafio, codigo)));
        Assert.Empty(_base.Sesiones);
    }
}
