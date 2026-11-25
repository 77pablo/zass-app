using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Credenciales;

/// <summary>
/// JQ-16 · CU-05 4D.3. Con el reloj de prueba: día 82 nada; 83 un aviso; 84 el mismo aviso y no
/// otro; 90 borrada y sin archivos. Y nunca un borrado sin aviso previo.
/// </summary>
public class BorradoresAbandonadosPruebas
{
    private static readonly DateTime Creada = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly ColaEnMemoria _avisos = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly AlmacenEnMemoria _almacen = new();
    private readonly RelojFijo _reloj = new(Creada);
    private readonly ServicioBorradoresAbandonados _servicio;
    private readonly Usuario _duena = new() { Correo = "duena@zass.local", Nombre = "Rosa" };
    private readonly PerfilPrestador _perfil;

    public BorradoresAbandonadosPruebas()
    {
        _perfil = new PerfilPrestador { UsuarioId = _duena.Id, NombrePublico = "Rosa", Slug = "rosa" };
        _base.Usuarios.Add(_duena);
        _base.PerfilesPrestador.Add(_perfil);
        _servicio = new ServicioBorradoresAbandonados(
            new BorradoresEnMemoria(_base), new RepositorioCredencialesEnMemoria(_base),
            _avisos, _base, _auditoria, _reloj, _almacen);
    }

    private Credencial UnBorrador(DateTime creada, params DateTime[] documentos)
    {
        var c = new Credencial
        {
            PerfilPrestadorId = _perfil.Id, Nombre = "Curso de gasfitería", Tipo = TipoCredencial.Curso,
            ObtenidaEn = new DateOnly(2025, 1, 1), Estado = EstadoCredencial.Borrador, CreadaEn = creada,
            PerfilPrestador = _perfil,
        };
        foreach (var d in documentos)
        {
            var clave = $"credenciales/{c.Id}/{Guid.NewGuid():N}.pdf";
            c.Documentos.Add(new DocumentoCredencial { CredencialId = c.Id, ClaveAlmacenamiento = clave, SubidoEn = d });
            _almacen.Archivos.Add(clave);
        }
        _base.Credenciales.Add(c);
        return c;
    }

    private Task<string> AlDiaAsync(double dias)
    {
        _reloj.Avanzar(Creada.AddDays(dias) - _reloj.Ahora);
        return _servicio.CorrerAsync();
    }

    private List<Notificacion> AvisosDe(Credencial c) =>
        _avisos.Encoladas.Where(n => n.ClaveIdempotencia.Contains(c.Id.ToString())).ToList();

    [Fact]
    public async Task Dia_82_nada_83_un_aviso_84_el_mismo_90_borrada_y_sin_archivos()
    {
        var borrador = UnBorrador(Creada, Creada.AddHours(1));
        var archivo = borrador.Documentos[0].ClaveAlmacenamiento;
        // La actividad es el documento (una hora después de crearla): los días se cuentan desde ahí.

        await AlDiaAsync(82);
        Assert.Empty(AvisosDe(borrador));

        await AlDiaAsync(83.1);
        var aviso = Assert.Single(AvisosDe(borrador));
        Assert.Equal(TipoNotificacion.BorradorPorEliminarse, aviso.Tipo);
        Assert.Equal(_duena.Id, aviso.DestinatarioId);
        Assert.Equal("Curso de gasfitería", CargaDeAviso.LeerCampo(aviso.Cuerpo, "credencial"));
        Assert.Contains(borrador, _base.Credenciales);

        await AlDiaAsync(84);
        Assert.Single(AvisosDe(borrador));
        Assert.Contains(borrador, _base.Credenciales);

        await AlDiaAsync(90.1);
        Assert.DoesNotContain(borrador, _base.Credenciales);
        Assert.DoesNotContain(archivo, _almacen.Archivos);
        Assert.Contains(_auditoria.Registros, r => r.Accion == "eliminar_borrador_abandonado" && r.EntidadId == borrador.Id);
    }

    [Fact]
    public async Task Nunca_borra_sin_aviso_aunque_hayan_pasado_los_90_dias()
    {
        // El planificador estuvo caído: la primera corrida es el día 95.
        var borrador = UnBorrador(Creada);

        await AlDiaAsync(95);
        Assert.Contains(borrador, _base.Credenciales);
        Assert.Single(AvisosDe(borrador));

        // Un día después del aviso todavía no: faltan 7 días desde el aviso.
        await AlDiaAsync(96);
        Assert.Contains(borrador, _base.Credenciales);

        await AlDiaAsync(102);
        Assert.DoesNotContain(borrador, _base.Credenciales);
        Assert.Single(AvisosDe(borrador));
    }

    [Fact]
    public async Task Subir_un_documento_despues_del_aviso_reinicia_el_plazo()
    {
        var borrador = UnBorrador(Creada);
        await AlDiaAsync(83.1);
        Assert.Single(AvisosDe(borrador));

        // Día 85: sube un documento. La actividad pasa a ser el día 85.
        borrador.Documentos.Add(new DocumentoCredencial { CredencialId = borrador.Id, ClaveAlmacenamiento = "x", SubidoEn = Creada.AddDays(85) });

        await AlDiaAsync(91);
        Assert.Contains(borrador, _base.Credenciales);
        Assert.Single(AvisosDe(borrador));

        // 83 días después de la nueva actividad, un aviso NUEVO (otra clave).
        await AlDiaAsync(85 + 83.1);
        Assert.Equal(2, AvisosDe(borrador).Select(a => a.ClaveIdempotencia).Distinct().Count());
    }

    [Fact]
    public async Task Lo_que_no_es_borrador_no_se_toca()
    {
        var enRevision = UnBorrador(Creada);
        enRevision.Estado = EstadoCredencial.EnRevision;

        await AlDiaAsync(83.1);
        await AlDiaAsync(120);

        Assert.Empty(AvisosDe(enRevision));
        Assert.Contains(enRevision, _base.Credenciales);
    }

    [Fact]
    public void La_clave_es_la_misma_para_la_misma_actividad_y_distinta_si_cambia()
    {
        var id = Guid.NewGuid();
        Assert.Equal(ServicioBorradoresAbandonados.ClaveDelAviso(id, Creada), ServicioBorradoresAbandonados.ClaveDelAviso(id, Creada));
        Assert.NotEqual(ServicioBorradoresAbandonados.ClaveDelAviso(id, Creada), ServicioBorradoresAbandonados.ClaveDelAviso(id, Creada.AddDays(1)));
    }

    // ------------------------------------------------------------- dobles

    /// <summary>La misma regla que RepositorioBorradoresEfCore, en memoria. La del SQL se prueba en integración.</summary>
    private sealed class BorradoresEnMemoria : IRepositorioBorradores
    {
        private readonly BaseEnMemoria _base;
        public BorradoresEnMemoria(BaseEnMemoria b) => _base = b;

        public Task<IReadOnlyList<BorradorInactivo>> InactivosAsync(DateTime actividadHasta, CancellationToken ct = default)
        {
            IReadOnlyList<BorradorInactivo> r = _base.Credenciales
                .Where(c => c.Estado == EstadoCredencial.Borrador)
                .Select(c => new BorradorInactivo(
                    c.Id,
                    _base.PerfilesPrestador.Single(p => p.Id == c.PerfilPrestadorId).UsuarioId,
                    c.Nombre,
                    c.Documentos.Select(d => d.SubidoEn).Append(c.CreadaEn).Max()))
                .Where(b => b.UltimaActividad <= actividadHasta)
                .ToList();
            return Task.FromResult(r);
        }
    }

    private sealed class AlmacenEnMemoria : IAlmacenDeArchivos
    {
        public HashSet<string> Archivos { get; } = new();
        public Task<Resultado> GuardarAsync(string clave, Stream contenido, string tipoMime, CancellationToken ct = default)
        {
            Archivos.Add(clave);
            return Task.FromResult(Resultado.Ok());
        }
        public Task<ArchivoAbierto?> AbrirAsync(string clave, CancellationToken ct = default) => Task.FromResult<ArchivoAbierto?>(null);
        public Task<Resultado> BorrarAsync(string clave, CancellationToken ct = default)
        {
            Archivos.Remove(clave);
            return Task.FromResult(Resultado.Ok());
        }
        public async IAsyncEnumerable<ArchivoListado> ListarAsync(string prefijo, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
