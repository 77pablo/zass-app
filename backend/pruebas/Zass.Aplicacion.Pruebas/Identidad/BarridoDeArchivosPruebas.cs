using System.Runtime.CompilerServices;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>
/// Auditoría 22-nov-2026: el barrido de archivos huérfanos. Lo que más importa es lo que NO borra:
/// lo que una fila usa, lo que es demasiado nuevo, y todo cuando la base no se pudo leer.
/// </summary>
public class BarridoDeArchivosPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Margen = TimeSpan.FromHours(24);

    private readonly BaseEnMemoria _base = new();
    private readonly AlmacenConFechas _almacen = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly RegistroQueAnota<ServicioBarridoDeArchivos> _registro = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly ClavesEnMemoria _claves;
    private readonly ServicioBarridoDeArchivos _servicio;

    public BarridoDeArchivosPruebas()
    {
        _claves = new ClavesEnMemoria(_base);
        _servicio = new ServicioBarridoDeArchivos(_claves, _almacen, _base, _auditoria, _reloj, _registro);
    }

    private static string Foto(string sufijo = "") => $"fotos/{Guid.NewGuid():N}{sufijo}.webp";
    private static string Documento() => $"documentos/{Guid.NewGuid():N}.pdf";

    private static DateTime Vieja => Ahora - Margen - TimeSpan.FromHours(1);
    private static DateTime Nueva => Ahora - TimeSpan.FromHours(1);

    private Task<string> Barrer(int tope = 100) => _servicio.CorrerAsync(Margen, tope);

    /// <summary>
    /// Revisión del 25-nov-2026: con «$» la forma de clave aceptaba un salto de línea al final (en
    /// .NET, «$» calza antes de un \n final). Con «\z», la clave termina donde termina el texto.
    /// </summary>
    [Fact]
    public void La_forma_de_clave_no_acepta_un_salto_de_linea_al_final()
    {
        var clave = Foto();
        Assert.True(TiposDeArchivo.EsClaveValida(clave));
        Assert.False(TiposDeArchivo.EsClaveValida(clave + "\n"));
        Assert.False(TiposDeArchivo.EsClaveValida(Documento() + "\n"));
    }

    [Fact]
    public async Task Un_archivo_que_ninguna_fila_referencia_y_es_mas_viejo_que_el_margen_se_borra()
    {
        var huerfano = Foto();
        var documento = Documento();
        _almacen.Poner(huerfano, Vieja);
        _almacen.Poner(documento, Vieja);

        var resumen = await Barrer();

        Assert.False(_almacen.Existe(huerfano));
        Assert.False(_almacen.Existe(documento));
        Assert.Equal("archivos huérfanos: 2 revisados, 2 borrados", resumen);
        var anotado = Assert.Single(_auditoria.Registros, r => r.Accion == "barrer_archivos_huerfanos");
        Assert.Contains("\"borrados\":2", anotado.Contexto);
        Assert.Equal(1, _base.Guardados);
    }

    [Fact]
    public async Task Un_archivo_en_uso_no_se_borra_ni_el_de_un_perfil_ni_el_de_un_trabajo_ni_el_de_un_documento()
    {
        var perfilGrande = Foto();
        var perfilChica = TiposDeArchivo.ClaveChica(perfilGrande);
        var trabajo = Foto();
        var documento = Documento();

        var perfil = new PerfilPrestador
        {
            NombrePublico = "Rosa", Slug = "rosa",
            UrlFoto = TiposDeArchivo.RutaPublica + perfilGrande[(TiposDeArchivo.CarpetaFotos.Length + 1)..],
        };
        _base.PerfilesPrestador.Add(perfil);
        _base.FotosDeTrabajo.Add(new FotoTrabajo { PerfilPrestadorId = perfil.Id, ClaveAlmacen = trabajo });
        var credencial = new Credencial { PerfilPrestadorId = perfil.Id, Nombre = "Curso" };
        credencial.Documentos.Add(new DocumentoCredencial { CredencialId = credencial.Id, ClaveAlmacenamiento = documento });
        _base.Credenciales.Add(credencial);

        foreach (var c in new[] { perfilGrande, perfilChica, trabajo, documento }) _almacen.Poner(c, Vieja);

        var resumen = await Barrer();

        Assert.True(_almacen.Existe(perfilGrande));
        Assert.True(_almacen.Existe(perfilChica));
        Assert.True(_almacen.Existe(trabajo));
        Assert.True(_almacen.Existe(documento));
        Assert.Equal("archivos huérfanos: 4 revisados, 0 borrados", resumen);
        Assert.Empty(_auditoria.Registros);
    }

    [Fact]
    public async Task Un_huerfano_mas_nuevo_que_el_margen_no_se_borra_porque_puede_ser_una_subida_en_curso()
    {
        var reciente = Foto();
        var viejo = Foto();
        _almacen.Poner(reciente, Nueva);
        _almacen.Poner(viejo, Vieja);

        await Barrer();

        Assert.True(_almacen.Existe(reciente));
        Assert.False(_almacen.Existe(viejo));
    }

    [Fact]
    public async Task Si_la_base_no_se_puede_leer_no_se_borra_nada()
    {
        var huerfano = Foto();
        _almacen.Poner(huerfano, Vieja);
        _claves.Falla = true;

        var resumen = await Barrer();

        Assert.True(_almacen.Existe(huerfano));
        Assert.Empty(_almacen.Borrados);
        Assert.Contains("nada borrado", resumen);
        Assert.Contains(_registro.Entradas, e => e.Nivel == Microsoft.Extensions.Logging.LogLevel.Error);
        Assert.Empty(_auditoria.Registros);
    }

    [Fact]
    public async Task Se_respeta_el_tope_por_corrida_y_lo_deja_anotado()
    {
        for (var i = 0; i < 5; i++) _almacen.Poner(Foto(), Vieja);

        var resumen = await Barrer(tope: 3);

        Assert.Equal(3, _almacen.Borrados.Count);
        Assert.Equal(2, _almacen.Cantidad);
        Assert.EndsWith("(tope alcanzado)", resumen);
        Assert.Contains(_registro.Entradas, e => e.Nivel == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("\"topeAlcanzado\":true", Assert.Single(_auditoria.Registros).Contexto);
    }

    [Fact]
    public async Task Nunca_toca_lo_que_esta_fuera_de_los_prefijos_conocidos_ni_claves_con_otra_forma()
    {
        _almacen.Poner("otra-cosa/" + Guid.NewGuid().ToString("N") + ".pdf", Vieja);
        _almacen.Poner("fotos/no-tiene-la-forma.webp", Vieja);
        _almacen.Poner("documentos/../fotos/x.pdf", Vieja);

        await Barrer();

        Assert.Empty(_almacen.Borrados);
    }

    // ------------------------------------------------------------- dobles

    private sealed class ClavesEnMemoria : IRepositorioClavesDeArchivos
    {
        private readonly BaseEnMemoria _base;
        public bool Falla { get; set; }
        public ClavesEnMemoria(BaseEnMemoria b) => _base = b;

        public Task<ClavesDeArchivosEnUso> EnUsoAsync(CancellationToken ct = default)
        {
            if (Falla) throw new InvalidOperationException("la base no contesta");
            return Task.FromResult(new ClavesDeArchivosEnUso(
                _base.PerfilesPrestador.Where(p => p.UrlFoto != null).Select(p => p.UrlFoto!).ToList(),
                _base.FotosDeTrabajo.Select(f => f.ClaveAlmacen).ToList(),
                _base.Credenciales.SelectMany(c => c.Documentos).Select(d => d.ClaveAlmacenamiento).ToList()));
        }
    }

    private sealed class AlmacenConFechas : IAlmacenDeArchivos
    {
        private readonly Dictionary<string, DateTime> _archivos = new();
        public List<string> Borrados { get; } = new();
        public int Cantidad => _archivos.Count;

        public void Poner(string clave, DateTime fecha) => _archivos[clave] = fecha;
        public bool Existe(string clave) => _archivos.ContainsKey(clave);

        public Task<Resultado> GuardarAsync(string clave, Stream contenido, string tipoMime, CancellationToken ct = default)
        {
            _archivos[clave] = DateTime.UtcNow;
            return Task.FromResult(Resultado.Ok());
        }

        public Task<ArchivoAbierto?> AbrirAsync(string clave, CancellationToken ct = default) =>
            Task.FromResult<ArchivoAbierto?>(null);

        public Task<Resultado> BorrarAsync(string clave, CancellationToken ct = default)
        {
            Borrados.Add(clave);
            _archivos.Remove(clave);
            return Task.FromResult(Resultado.Ok());
        }

        public async IAsyncEnumerable<ArchivoListado> ListarAsync(
            string prefijo, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            foreach (var (clave, fecha) in _archivos.ToList())
                if (clave.StartsWith(prefijo, StringComparison.Ordinal))
                    yield return new ArchivoListado(clave, fecha);
        }
    }
}
