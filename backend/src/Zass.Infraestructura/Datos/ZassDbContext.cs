using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Zass.Dominio.Agenda;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;
using Zass.Dominio.Reputacion;
using Zass.Dominio.Suscripciones;
using Zass.Dominio.Tarjetas;

namespace Zass.Infraestructura.Datos;

public class ZassDbContext : DbContext
{
    public ZassDbContext(DbContextOptions<ZassDbContext> opciones) : base(opciones) { }

    // S1 · Identidad y perfiles
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<UsuarioRol> UsuarioRoles => Set<UsuarioRol>();
    public DbSet<Sesion> Sesiones => Set<Sesion>();
    public DbSet<PerfilPrestador> PerfilesPrestador => Set<PerfilPrestador>();
    public DbSet<PerfilBuscador> PerfilesBuscador => Set<PerfilBuscador>();

    // Catalogo y geografia
    public DbSet<Rubro> Rubros => Set<Rubro>();
    public DbSet<Servicio> Servicios => Set<Servicio>();
    public DbSet<PrestadorServicio> PrestadorServicios => Set<PrestadorServicio>();
    public DbSet<Zona> Zonas => Set<Zona>();
    public DbSet<CoberturaPrestador> Coberturas => Set<CoberturaPrestador>();

    // S2 · Credenciales
    public DbSet<EntidadCertificadora> EntidadesCertificadoras => Set<EntidadCertificadora>();
    public DbSet<Credencial> Credenciales => Set<Credencial>();
    public DbSet<DocumentoCredencial> DocumentosCredencial => Set<DocumentoCredencial>();
    public DbSet<Verificacion> Verificaciones => Set<Verificacion>();

    // S4 · Agenda
    public DbSet<Disponibilidad> Disponibilidades => Set<Disponibilidad>();
    public DbSet<BloqueHorario> Bloques => Set<BloqueHorario>();
    public DbSet<BloqueoAgenda> BloqueosAgenda => Set<BloqueoAgenda>();
    public DbSet<Cita> Citas => Set<Cita>();
    public DbSet<CambioCita> CambiosCita => Set<CambioCita>();

    // S5 · Reputacion
    public DbSet<Contacto> Contactos => Set<Contacto>();
    public DbSet<Resena> Resenas => Set<Resena>();
    public DbSet<RespuestaResena> RespuestasResena => Set<RespuestaResena>();
    public DbSet<ReporteResena> ReportesResena => Set<ReporteResena>();

    // S6 · Tarjetas
    public DbSet<Tarjeta> Tarjetas => Set<Tarjeta>();
    public DbSet<EscaneoTarjeta> Escaneos => Set<EscaneoTarjeta>();

    // S7 · Suscripciones
    public DbSet<Plan> Planes => Set<Plan>();
    public DbSet<Suscripcion> Suscripciones => Set<Suscripcion>();
    public DbSet<Pago> Pagos => Set<Pago>();

    // Transversal
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("postgis");
        b.HasPostgresExtension("citext");

        ConfigurarIdentidad(b);
        ConfigurarCatalogo(b);
        ConfigurarCredenciales(b);
        ConfigurarAgenda(b);
        ConfigurarReputacion(b);
        ConfigurarTarjetas(b);
        ConfigurarSuscripciones(b);
        ConfigurarSoporte(b);

        AplicarConvenciones(b);
    }

    // ---------------------------------------------------------------- S1

    private static void ConfigurarIdentidad(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.HasKey(x => x.Id);
            // citext: Pablo@correo.cl y pablo@correo.cl son la misma persona
            e.Property(x => x.Correo).HasColumnType("citext").IsRequired();
            e.HasIndex(x => x.Correo).IsUnique();
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.TokenVerificacion);
            e.HasIndex(x => x.TokenRecuperacion);
        });

        b.Entity<Rol>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.Codigo).HasMaxLength(30).IsRequired();

            // TBL-02 es catalogo, no dato de ejemplo: va en el esquema y no en una
            // siembra aparte. Sin estas cinco filas ningun registro puede completarse,
            // porque ServicioRegistro busca el rol por codigo antes de crear la cuenta.
            // Los identificadores son fijos a proposito (ValueGeneratedNever).
            e.HasData(
                new Rol { Id = 1, Codigo = "buscador",      Nombre = "Buscador",      EsInterno = false },
                new Rol { Id = 2, Codigo = "prestador",     Nombre = "Prestador",     EsInterno = false },
                new Rol { Id = 3, Codigo = "verificador",   Nombre = "Verificador",   EsInterno = true  },
                new Rol { Id = 4, Codigo = "moderador",     Nombre = "Moderador",     EsInterno = true  },
                new Rol { Id = 5, Codigo = "administrador", Nombre = "Administrador", EsInterno = true  });
        });

        b.Entity<UsuarioRol>(e =>
        {
            e.HasKey(x => new { x.UsuarioId, x.RolId });
            e.HasOne(x => x.Usuario).WithMany(u => u.Roles)
                .HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Rol).WithMany().HasForeignKey(x => x.RolId);
        });

        b.Entity<Sesion>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.HashToken).IsUnique();
            e.HasIndex(x => x.UsuarioId);
            e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PerfilPrestador>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UsuarioId).IsUnique();
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            e.Property(x => x.NombrePublico).HasMaxLength(150).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(2000);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.OrigenUbicacion).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PromedioCalificacion).HasPrecision(2, 1);

            // CC-02 · version optimista
            e.Property(x => x.Version).IsConcurrencyToken();

            // `ubicacion_exacta` existe en la base pero NO esta en este modelo, a
            // proposito. La genera PostgreSQL desde latitud y longitud, y la crea la
            // migracion UbicacionFueraDelModelo con SQL directo, junto con su indice
            // GIST.
            //
            // Por que no mapearla: al declararla aqui, EF Core intentaba leerla de
            // vuelta despues de cada INSERT y fallaba, porque `geography` no se
            // convierte a string. La alternativa era meter NetTopologySuite en el
            // mapeo, y la decision del documento es justamente la contraria: la
            // busqueda por cercania va con FromSqlInterpolated, que no pasa por el
            // modelo. Una columna que ninguna consulta de EF Core necesita no tiene
            // por que estar en el modelo.

            e.HasIndex(x => new { x.Estado, x.RubroId });
            e.HasOne(x => x.Usuario).WithOne(u => u.PerfilPrestador)
                .HasForeignKey<PerfilPrestador>(x => x.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Rubro).WithMany().HasForeignKey(x => x.RubroId);
        });

        b.Entity<PerfilBuscador>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UsuarioId).IsUnique();
            e.HasOne(x => x.Usuario).WithOne(u => u.PerfilBuscador)
                .HasForeignKey<PerfilBuscador>(x => x.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ZonaInteres).WithMany().HasForeignKey(x => x.ZonaInteresId);
        });
    }

    // ---------------------------------------------------------- Catalogo

    private static void ConfigurarCatalogo(ModelBuilder b)
    {
        b.Entity<Rubro>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(80).IsRequired();
        });

        b.Entity<Servicio>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            e.HasOne(x => x.Rubro).WithMany(r => r.Servicios).HasForeignKey(x => x.RubroId);
            e.HasIndex(x => x.RubroId);
        });

        b.Entity<PrestadorServicio>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PerfilPrestadorId, x.ServicioId }).IsUnique();
            e.Property(x => x.ModoAgenda).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.PerfilPrestador).WithMany(p => p.Servicios)
                .HasForeignKey(x => x.PerfilPrestadorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Servicio).WithMany().HasForeignKey(x => x.ServicioId);
        });

        b.Entity<Zona>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            e.Property(x => x.Sector).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.Sector);
        });

        b.Entity<CoberturaPrestador>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PerfilPrestadorId, x.ZonaId }).IsUnique();
            e.HasIndex(x => new { x.ZonaId, x.PerfilPrestadorId });
            // R-2 · una sola zona principal por prestador
            e.HasIndex(x => x.PerfilPrestadorId)
                .IsUnique()
                .HasFilter("es_principal")
                .HasDatabaseName("ux_cobertura_principal");
            e.HasOne(x => x.PerfilPrestador).WithMany(p => p.Cobertura)
                .HasForeignKey(x => x.PerfilPrestadorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Zona).WithMany().HasForeignKey(x => x.ZonaId);
        });
    }

    // ---------------------------------------------------------------- S2

    private static void ConfigurarCredenciales(ModelBuilder b)
    {
        b.Entity<EntidadCertificadora>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Nombre).IsUnique();
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
        });

        b.Entity<Credencial>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.PerfilPrestadorId, x.Estado });
            e.HasIndex(x => new { x.Estado, x.CreadaEn });

            // R-5 · no puede vencer antes de obtenerse
            e.ToTable(t => t.HasCheckConstraint("ck_credencial_fechas",
                "vence_en IS NULL OR vence_en > obtenida_en"));
            // R-6 · verificada exige verificador
            e.ToTable(t => t.HasCheckConstraint("ck_credencial_sello",
                "estado <> 'Verificada' OR sello_por IS NOT NULL"));

            e.HasOne(x => x.PerfilPrestador).WithMany()
                .HasForeignKey(x => x.PerfilPrestadorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.EntidadCertificadora).WithMany()
                .HasForeignKey(x => x.EntidadCertificadoraId);
        });

        b.Entity<DocumentoCredencial>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ClaveAlmacenamiento).IsUnique();   // R-9
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(30);
            e.HasOne(x => x.Credencial).WithMany(c => c.Documentos)
                .HasForeignKey(x => x.CredencialId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Verificacion>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Resultado).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Motivo).HasConversion<string>().HasMaxLength(40);
            // R-7 · un rechazo no existe sin motivo
            e.ToTable(t => t.HasCheckConstraint("ck_verificacion_motivo",
                "resultado <> 'Rechazada' OR motivo IS NOT NULL"));
            e.HasOne(x => x.Credencial).WithMany(c => c.Verificaciones)
                .HasForeignKey(x => x.CredencialId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    // ---------------------------------------------------------------- S4

    private static void ConfigurarAgenda(ModelBuilder b)
    {
        b.Entity<Disponibilidad>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PerfilPrestadorId, x.DiaSemana });
            e.HasOne(x => x.PerfilPrestador).WithMany()
                .HasForeignKey(x => x.PerfilPrestadorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PrestadorServicio).WithMany()
                .HasForeignKey(x => x.PrestadorServicioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BloqueHorario>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            // R-3 · sin bloques invertidos ni de duracion cero
            e.ToTable(t => t.HasCheckConstraint("ck_bloque_rango", "fin_en > inicio_en"));
            e.HasIndex(x => new { x.PerfilPrestadorId, x.InicioEn });
            e.HasIndex(x => new { x.PrestadorServicioId, x.InicioEn, x.Estado });
            e.HasOne(x => x.PerfilPrestador).WithMany()
                .HasForeignKey(x => x.PerfilPrestadorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PrestadorServicio).WithMany()
                .HasForeignKey(x => x.PrestadorServicioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BloqueoAgenda>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PerfilPrestadorId, x.DesdeEn });
        });

        b.Entity<Cita>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.Codigo).HasMaxLength(12).IsRequired();
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Modo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.NotaBuscador).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();          // CC-02

            // ---------------------------------------------------------------
            // R-1 · LA RESTRICCION MAS IMPORTANTE DEL SISTEMA
            // Un bloque admite exactamente una cita activa. Indice unico parcial:
            // solo cuentan los estados que ocupan el bloque. Si el codigo tuviera
            // un error, la base igual se niega.
            // ---------------------------------------------------------------
            e.HasIndex(x => x.BloqueHorarioId)
                .IsUnique()
                .HasFilter("estado IN ('Solicitada','Confirmada')")
                .HasDatabaseName("ux_cita_bloque_activo");

            // CC-04 · idempotencia
            e.HasIndex(x => x.ClaveIdempotencia).IsUnique()
                .HasFilter("clave_idempotencia IS NOT NULL");

            // R-10 · ninguna cita es origen de si misma
            e.ToTable(t => t.HasCheckConstraint("ck_cita_origen",
                "cita_origen_id IS NULL OR cita_origen_id <> id"));

            e.HasIndex(x => new { x.BuscadorId, x.Estado });
            e.HasIndex(x => new { x.PerfilPrestadorId, x.Estado });
            e.HasIndex(x => x.CitaOrigenId);

            // R-8 · no se borra un bloque con cita viva
            e.HasOne(x => x.BloqueHorario).WithOne(bl => bl.Cita)
                .HasForeignKey<Cita>(x => x.BloqueHorarioId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Buscador).WithMany().HasForeignKey(x => x.BuscadorId);
            e.HasOne(x => x.PrestadorServicio).WithMany()
                .HasForeignKey(x => x.PrestadorServicioId);
            e.HasOne(x => x.PerfilPrestador).WithMany()
                .HasForeignKey(x => x.PerfilPrestadorId);
        });

        b.Entity<CambioCita>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.EstadoAnterior).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.EstadoNuevo).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.CitaId, x.OcurridoEn });
            e.HasOne<Cita>().WithMany(c => c.Cambios)
                .HasForeignKey(x => x.CitaId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    // ---------------------------------------------------------------- S5

    private static void ConfigurarReputacion(ModelBuilder b)
    {
        b.Entity<Contacto>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Origen).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Canal).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.BuscadorId, x.PerfilPrestadorId, x.OcurridoEn });
            e.HasOne(x => x.Buscador).WithMany().HasForeignKey(x => x.BuscadorId);
            e.HasOne(x => x.PerfilPrestador).WithMany()
                .HasForeignKey(x => x.PerfilPrestadorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Cita).WithMany().HasForeignKey(x => x.CitaId);
        });

        b.Entity<Resena>(e =>
        {
            e.HasKey(x => x.Id);
            // R-11 · obligatorio y unico. Media defensa de la elegibilidad.
            e.HasIndex(x => x.ContactoId).IsUnique();
            e.Property(x => x.Comentario).HasMaxLength(1500).IsRequired();
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(30);
            // R-12 y R-13
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_resena_calificacion",
                    "calificacion BETWEEN 1 AND 5");
                t.HasCheckConstraint("ck_resena_comentario",
                    "char_length(comentario) BETWEEN 20 AND 1500");
            });
            e.HasIndex(x => new { x.PerfilPrestadorId, x.Estado, x.PublicadaEn });
            e.HasOne(x => x.Contacto).WithOne(c => c.Resena)
                .HasForeignKey<Resena>(x => x.ContactoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PerfilPrestador).WithMany()
                .HasForeignKey(x => x.PerfilPrestadorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Autor).WithMany().HasForeignKey(x => x.AutorId);
        });

        b.Entity<RespuestaResena>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ResenaId).IsUnique();     // R-14
            e.Property(x => x.Texto).HasMaxLength(1000).IsRequired();
            e.HasOne(x => x.Resena).WithOne(r => r.Respuesta)
                .HasForeignKey<RespuestaResena>(x => x.ResenaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ReporteResena>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Motivo).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(30);
            // R-19 · nadie resuelve su propio reporte
            e.ToTable(t => t.HasCheckConstraint("ck_reporte_moderador",
                "resuelto_por IS NULL OR resuelto_por <> reportado_por"));
            e.HasIndex(x => x.Estado);
            e.HasOne(x => x.Resena).WithMany(r => r.Reportes)
                .HasForeignKey(x => x.ResenaId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    // ---------------------------------------------------------------- S6

    private static void ConfigurarTarjetas(ModelBuilder b)
    {
        b.Entity<Tarjeta>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UrlEstable).IsUnique();   // R-15
            e.Property(x => x.UrlEstable).HasMaxLength(20).IsRequired();
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            // R-16 · dos tarjetas vivas no comparten codigo
            e.HasIndex(x => x.HashCodigoActivacion).IsUnique()
                .HasFilter("estado = 'Emitida' AND hash_codigo_activacion IS NOT NULL");
            // R-17 · tarjeta activa sin dueno es imposible
            e.ToTable(t => t.HasCheckConstraint("ck_tarjeta_dueno",
                "estado <> 'Activa' OR perfil_prestador_id IS NOT NULL"));
            e.HasOne(x => x.PerfilPrestador).WithMany()
                .HasForeignKey(x => x.PerfilPrestadorId);
        });

        b.Entity<EscaneoTarjeta>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Medio).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(x => new { x.TarjetaId, x.OcurridoEn });
            e.HasOne(x => x.Tarjeta).WithMany(t => t.Escaneos)
                .HasForeignKey(x => x.TarjetaId).OnDelete(DeleteBehavior.Cascade);
            // Sin columna de identidad del visitante (R-20).
        });
    }

    // ---------------------------------------------------------------- S7

    private static void ConfigurarSuscripciones(ModelBuilder b)
    {
        b.Entity<Plan>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.Codigo).HasMaxLength(30).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(80).IsRequired();
        });

        b.Entity<Suscripcion>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            // Un prestador, una suscripcion vigente
            e.HasIndex(x => x.PerfilPrestadorId).IsUnique()
                .HasFilter("estado IN ('Prueba','Activa')")
                .HasDatabaseName("ux_suscripcion_vigente");
            e.HasIndex(x => new { x.Estado, x.VigenteHasta });
            e.HasOne(x => x.PerfilPrestador).WithMany()
                .HasForeignKey(x => x.PerfilPrestadorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId);
        });

        b.Entity<Pago>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Moneda).HasMaxLength(3);
            e.HasIndex(x => new { x.SuscripcionId, x.CreadoEn });
            e.HasOne(x => x.Suscripcion).WithMany(s => s.Pagos)
                .HasForeignKey(x => x.SuscripcionId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    // --------------------------------------------------------- Transversal

    private static void ConfigurarSoporte(ModelBuilder b)
    {
        b.Entity<Notificacion>(e =>
        {
            e.HasKey(x => x.Id);
            // R-18 · impide el aviso duplicado
            e.HasIndex(x => x.ClaveIdempotencia).IsUnique();
            e.Property(x => x.ClaveIdempotencia).HasMaxLength(200).IsRequired();
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Canal).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Asunto).HasMaxLength(200);
            e.HasIndex(x => new { x.Estado, x.ProgramadaPara });
            e.HasIndex(x => new { x.DestinatarioId, x.EnviadaEn });
            e.HasOne(x => x.Destinatario).WithMany()
                .HasForeignKey(x => x.DestinatarioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RegistroAuditoria>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Accion).HasMaxLength(80).IsRequired();
            e.Property(x => x.Entidad).HasMaxLength(60).IsRequired();
            e.Property(x => x.Contexto).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Entidad, x.EntidadId, x.OcurridoEn });
            e.HasIndex(x => new { x.ActorId, x.OcurridoEn });
        });
    }

    // ------------------------------------------------------- Convenciones

    /// <summary>
    /// Nombres en snake_case, igual que el modelo documentado, y listas de texto
    /// como arreglos de PostgreSQL.
    /// </summary>
    private static void AplicarConvenciones(ModelBuilder b)
    {
        foreach (var tipo in b.Model.GetEntityTypes())
        {
            var tabla = tipo.GetTableName();
            if (tabla is not null) tipo.SetTableName(ASnake(tabla));

            foreach (var prop in tipo.GetProperties())
                prop.SetColumnName(ASnake(prop.GetColumnName()));

            foreach (var clave in tipo.GetKeys())
                clave.SetName(ASnake(clave.GetName() ?? ""));

            foreach (var fk in tipo.GetForeignKeys())
                fk.SetConstraintName(ASnake(fk.GetConstraintName() ?? ""));

            foreach (var idx in tipo.GetIndexes())
                idx.SetDatabaseName(ASnake(idx.GetDatabaseName() ?? ""));
        }

        // Listas de texto -> text[]
        b.Entity<Resena>().Property(x => x.Aspectos).HasColumnType("text[]");
        b.Entity<PerfilBuscador>().Property(x => x.Favoritos)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null)
                     ?? new List<Guid>(),
                new ValueComparer<List<Guid>>(
                    (a, c) => a != null && c != null && a.SequenceEqual(c),
                    v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode())),
                    v => v.ToList()))
            .HasColumnType("jsonb");
    }

    private static string ASnake(string nombre)
    {
        if (string.IsNullOrEmpty(nombre)) return nombre;
        var sb = new StringBuilder(nombre.Length + 8);
        for (var i = 0; i < nombre.Length; i++)
        {
            var c = nombre[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && nombre[i - 1] != '_') sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
