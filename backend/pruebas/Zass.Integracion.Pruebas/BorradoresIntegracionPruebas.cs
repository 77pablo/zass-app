using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;
using Zass.Infraestructura.Datos;
using Zass.Infraestructura.Datos.Repositorios;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// JQ-16. La consulta de borradores inactivos contra PostgreSQL: la última actividad es la creación
/// o el último documento, y solo cuentan los Borrador. Fechas en 1990 para no cruzarse con filas
/// de otras pruebas; todo dentro de una transacción que se deshace.
/// </summary>
public class BorradoresIntegracionPruebas
{
    private static readonly DateTime Base = new(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task La_ultima_actividad_es_la_creacion_o_el_ultimo_documento()
    {
        using var prueba = new ContextoDePrueba();
        var ctx = prueba.Ctx;
        var usuario = new Usuario { Correo = $"bor-{Guid.NewGuid():N}@zass.local", HashContrasena = "x", Nombre = "Rosa", CorreoVerificado = true };
        ctx.Usuarios.Add(usuario);
        var perfil = new PerfilPrestador { UsuarioId = usuario.Id, NombrePublico = $"Rosa {Guid.NewGuid():N}", Slug = $"bor-{Guid.NewGuid():N}", RubroId = 1 };
        ctx.PerfilesPrestador.Add(perfil);

        Credencial Una(string nombre, EstadoCredencial estado, DateTime creada, params DateTime[] docs)
        {
            var c = new Credencial
            {
                PerfilPrestadorId = perfil.Id, Nombre = nombre, Tipo = TipoCredencial.Curso,
                ObtenidaEn = new DateOnly(1989, 1, 1), Estado = estado, CreadaEn = creada,
            };
            ctx.Credenciales.Add(c);
            foreach (var d in docs)
                ctx.DocumentosCredencial.Add(new DocumentoCredencial
                {
                    CredencialId = c.Id, ClaveAlmacenamiento = $"bor/{Guid.NewGuid():N}", NombreOriginal = "a.pdf",
                    TipoMime = "application/pdf", TamanoBytes = 1, Estado = EstadoDocumento.Disponible, SubidoEn = d,
                });
            return c;
        }

        var sinDocs = Una("Sin documentos", EstadoCredencial.Borrador, Base);
        var conDocViejo = Una("Documento viejo", EstadoCredencial.Borrador, Base, Base.AddDays(5), Base.AddDays(10));
        var conDocNuevo = Una("Documento nuevo", EstadoCredencial.Borrador, Base, Base.AddDays(50));
        var enRevision = Una("En revisión", EstadoCredencial.EnRevision, Base);
        await ctx.SaveChangesAsync();

        // Corte: actividad hasta el día 20.
        var r = await new RepositorioBorradoresEfCore(ctx).InactivosAsync(Base.AddDays(20));
        var mios = r.Where(b => new[] { sinDocs.Id, conDocViejo.Id, conDocNuevo.Id, enRevision.Id }.Contains(b.CredencialId)).ToList();

        Assert.Equal(2, mios.Count);
        Assert.Equal(Base, mios.Single(b => b.CredencialId == sinDocs.Id).UltimaActividad);
        Assert.Equal(Base.AddDays(10), mios.Single(b => b.CredencialId == conDocViejo.Id).UltimaActividad);
        Assert.All(mios, b => Assert.Equal(usuario.Id, b.UsuarioId));
    }
}
