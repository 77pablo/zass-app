using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Zass.Dominio.Agenda;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
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
    public DbSet<TrabajoDeFondo> TrabajosDeFondo => Set<TrabajoDeFondo>();

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
        ConfigurarBusqueda(b);
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
            e.Property(x => x.Descripcion).HasMaxLength(Limites.DescripcionMaxima);
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

            // TBL-07 es catalogo, no dato de ejemplo: va en el esquema, igual que los
            // roles de TBL-02 y el plan de prueba. La precondicion P4 de CU-03 pide que
            // exista al menos un rubro activo, asi que sin estas filas NINGUN prestador
            // puede publicar, y con RN-S3-09 eso es una busqueda permanentemente vacia.
            // Los identificadores son fijos a proposito (ValueGeneratedNever).
            e.HasData(
                new Rubro { Id = 1, Codigo = "corretaje", Nombre = "Corretaje de propiedades", Icono = "llave", Activo = true },
                new Rubro { Id = 2, Codigo = "derecho", Nombre = "Derecho", Icono = "balanza", Activo = true },
                new Rubro { Id = 3, Codigo = "contabilidad", Nombre = "Contabilidad", Icono = "calculadora", Activo = true },
                new Rubro { Id = 4, Codigo = "arquitectura", Nombre = "Arquitectura y construcción", Icono = "regla", Activo = true },
                new Rubro { Id = 5, Codigo = "oficios", Nombre = "Técnicos y oficios", Icono = "herramientas", Activo = true });
        });

        b.Entity<Servicio>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            e.HasOne(x => x.Rubro).WithMany(r => r.Servicios).HasForeignKey(x => x.RubroId);
            e.HasIndex(x => x.RubroId);

            e.Property(x => x.NombreNormalizado).HasMaxLength(160).IsRequired();

            // Dos servicios con el mismo nombre en un rubro son el mismo servicio: la
            // busqueda por rubro acabaria mostrando dos filas para lo mismo.
            //
            // La comparacion va sobre la columna normalizada y no sobre lower(nombre),
            // y eso NO es un detalle: este cluster se creo con locale C, donde lower()
            // solo plega ASCII. Un indice sobre lower(nombre) aceptaba
            // "TASACION COMERCIAL" al lado de "Tasacion comercial", y lo hacia depender
            // del locale del servidor, que en Azure sera otro. Lo encontro una prueba
            // de integracion que esperaba el rechazo y no lo obtuvo.
            e.HasIndex(x => new { x.RubroId, x.NombreNormalizado })
                .IsUnique()
                .HasDatabaseName("ux_servicio_rubro_nombre");

            // TBL-08. Los servicios del catalogo del equipo llevan es_catalogo = true;
            // los que agrega un prestador por API-20 entran con false y los crea la
            // aplicacion. Los identificadores van por bloques de cien, uno por rubro,
            // para que se lean de un vistazo en la base.
            //
            // CUIDADO: `servicios.id` es GENERATED BY DEFAULT AS IDENTITY, y sembrar con
            // identificadores explicitos NO mueve la secuencia. La migracion que trae
            // estas filas la reinicia por encima del maximo; sin eso, el primer servicio
            // que agregue un prestador pediria el id 1 y chocaria con la clave primaria.
            e.HasData(
                new Servicio { Id = 101, RubroId = 1, Nombre = "Tasación comercial", NombreNormalizado = "tasacion comercial", EsCatalogo = true },
                new Servicio { Id = 102, RubroId = 1, Nombre = "Administración de arriendos", NombreNormalizado = "administracion de arriendos", EsCatalogo = true },
                new Servicio { Id = 103, RubroId = 1, Nombre = "Venta de casa o departamento", NombreNormalizado = "venta de casa o departamento", EsCatalogo = true },
                new Servicio { Id = 104, RubroId = 1, Nombre = "Arriendo de propiedad", NombreNormalizado = "arriendo de propiedad", EsCatalogo = true },
                new Servicio { Id = 105, RubroId = 1, Nombre = "Corretaje de terrenos y parcelas", NombreNormalizado = "corretaje de terrenos y parcelas", EsCatalogo = true },
                new Servicio { Id = 106, RubroId = 1, Nombre = "Estudio de títulos", NombreNormalizado = "estudio de titulos", EsCatalogo = true },
                new Servicio { Id = 107, RubroId = 1, Nombre = "Gestión de crédito hipotecario", NombreNormalizado = "gestion de credito hipotecario", EsCatalogo = true },
                new Servicio { Id = 201, RubroId = 2, Nombre = "Escritura de compraventa", NombreNormalizado = "escritura de compraventa", EsCatalogo = true },
                new Servicio { Id = 202, RubroId = 2, Nombre = "Contrato de arriendo", NombreNormalizado = "contrato de arriendo", EsCatalogo = true },
                new Servicio { Id = 203, RubroId = 2, Nombre = "Constitución de sociedad", NombreNormalizado = "constitucion de sociedad", EsCatalogo = true },
                new Servicio { Id = 204, RubroId = 2, Nombre = "Divorcio de mutuo acuerdo", NombreNormalizado = "divorcio de mutuo acuerdo", EsCatalogo = true },
                new Servicio { Id = 205, RubroId = 2, Nombre = "Posesión efectiva", NombreNormalizado = "posesion efectiva", EsCatalogo = true },
                new Servicio { Id = 206, RubroId = 2, Nombre = "Defensa laboral", NombreNormalizado = "defensa laboral", EsCatalogo = true },
                new Servicio { Id = 207, RubroId = 2, Nombre = "Cobranza de deudas", NombreNormalizado = "cobranza de deudas", EsCatalogo = true },
                new Servicio { Id = 208, RubroId = 2, Nombre = "Revisión de contratos", NombreNormalizado = "revision de contratos", EsCatalogo = true },
                new Servicio { Id = 301, RubroId = 3, Nombre = "Declaración de renta", NombreNormalizado = "declaracion de renta", EsCatalogo = true },
                new Servicio { Id = 302, RubroId = 3, Nombre = "Declaración mensual de IVA", NombreNormalizado = "declaracion mensual de iva", EsCatalogo = true },
                new Servicio { Id = 303, RubroId = 3, Nombre = "Contabilidad mensual de empresa", NombreNormalizado = "contabilidad mensual de empresa", EsCatalogo = true },
                new Servicio { Id = 304, RubroId = 3, Nombre = "Liquidación de sueldos", NombreNormalizado = "liquidacion de sueldos", EsCatalogo = true },
                new Servicio { Id = 305, RubroId = 3, Nombre = "Inicio de actividades en el SII", NombreNormalizado = "inicio de actividades en el sii", EsCatalogo = true },
                new Servicio { Id = 306, RubroId = 3, Nombre = "Facturación electrónica", NombreNormalizado = "facturacion electronica", EsCatalogo = true },
                new Servicio { Id = 307, RubroId = 3, Nombre = "Finiquitos y trámites laborales", NombreNormalizado = "finiquitos y tramites laborales", EsCatalogo = true },
                new Servicio { Id = 401, RubroId = 4, Nombre = "Anteproyecto de vivienda", NombreNormalizado = "anteproyecto de vivienda", EsCatalogo = true },
                new Servicio { Id = 402, RubroId = 4, Nombre = "Permiso de edificación", NombreNormalizado = "permiso de edificacion", EsCatalogo = true },
                new Servicio { Id = 403, RubroId = 4, Nombre = "Regularización de ampliaciones", NombreNormalizado = "regularizacion de ampliaciones", EsCatalogo = true },
                new Servicio { Id = 404, RubroId = 4, Nombre = "Diseño de remodelación", NombreNormalizado = "diseno de remodelacion", EsCatalogo = true },
                new Servicio { Id = 405, RubroId = 4, Nombre = "Inspección técnica de obra", NombreNormalizado = "inspeccion tecnica de obra", EsCatalogo = true },
                new Servicio { Id = 406, RubroId = 4, Nombre = "Cálculo estructural", NombreNormalizado = "calculo estructural", EsCatalogo = true },
                new Servicio { Id = 407, RubroId = 4, Nombre = "Certificado de recepción final", NombreNormalizado = "certificado de recepcion final", EsCatalogo = true },
                new Servicio { Id = 501, RubroId = 5, Nombre = "Instalación eléctrica", NombreNormalizado = "instalacion electrica", EsCatalogo = true },
                new Servicio { Id = 502, RubroId = 5, Nombre = "Reparación de gasfitería", NombreNormalizado = "reparacion de gasfiteria", EsCatalogo = true },
                new Servicio { Id = 503, RubroId = 5, Nombre = "Instalación de calefón", NombreNormalizado = "instalacion de calefon", EsCatalogo = true },
                new Servicio { Id = 504, RubroId = 5, Nombre = "Pintura de interiores", NombreNormalizado = "pintura de interiores", EsCatalogo = true },
                new Servicio { Id = 505, RubroId = 5, Nombre = "Instalación de cerámica y piso flotante", NombreNormalizado = "instalacion de ceramica y piso flotante", EsCatalogo = true },
                new Servicio { Id = 506, RubroId = 5, Nombre = "Reparación de techumbre", NombreNormalizado = "reparacion de techumbre", EsCatalogo = true },
                new Servicio { Id = 507, RubroId = 5, Nombre = "Cerrajería", NombreNormalizado = "cerrajeria", EsCatalogo = true },
                new Servicio { Id = 508, RubroId = 5, Nombre = "Instalación de aire acondicionado", NombreNormalizado = "instalacion de aire acondicionado", EsCatalogo = true },
                new Servicio { Id = 509, RubroId = 5, Nombre = "Soldadura y estructuras metálicas", NombreNormalizado = "soldadura y estructuras metalicas", EsCatalogo = true },
                new Servicio { Id = 510, RubroId = 5, Nombre = "Mantención de estufas y calefacción", NombreNormalizado = "mantencion de estufas y calefaccion", EsCatalogo = true });
        });

        b.Entity<PrestadorServicio>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PerfilPrestadorId, x.ServicioId }).IsUnique();
            e.Property(x => x.ModoAgenda).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.PerfilPrestador).WithMany(p => p.Servicios)
                .HasForeignKey(x => x.PerfilPrestadorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Servicio).WithMany().HasForeignKey(x => x.ServicioId);

            // Los mismos limites que valida ServicioCatalogo, pero aqui los hace
            // cumplir la base: una duracion de 0 minutos o un precio negativo no
            // pueden entrar ni saltandose el servicio.
            e.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_prestador_servicio_duracion",
                    "duracion_minutos >= 10 AND duracion_minutos <= 480");
                t.HasCheckConstraint(
                    "ck_prestador_servicio_precio",
                    "precio_referencia IS NULL OR precio_referencia >= 0");
            });
        });

        b.Entity<Zona>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            e.Property(x => x.Sector).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.Sector);
            e.Property(x => x.Comuna).HasMaxLength(120).IsRequired();
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Region).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.Region);

            // Un barrio es de una comuna. Restrict: borrar una comuna que tiene barrios no es algo que deba pasar
            // por un descuido, y arrastrar las coberturas de la gente en cascada seria peor.
            e.HasOne<Zona>().WithMany().HasForeignKey(x => x.PadreId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.PadreId);

            // La jerarquia es de dos niveles y la base lo exige: una comuna no tiene padre, un barrio siempre.
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_zona_tipo", "tipo IN ('Comuna', 'Barrio')");
                t.HasCheckConstraint("ck_zona_padre", "(tipo = 'Comuna' AND padre_id IS NULL) OR (tipo = 'Barrio' AND padre_id IS NOT NULL)");
            });

            // Dos zonas con el mismo nombre en la misma comuna son la misma zona, y en
            // un selector serian indistinguibles. La comparacion es exacta a proposito:
            // este cluster tiene locale C y lower() solo pliega ASCII, asi que un unico
            // sobre lower(nombre) no haria lo que parece (ver TBL-08).
            e.HasIndex(x => new { x.Comuna, x.Nombre }).IsUnique();

            // TBL-10. Las zonas son catalogo, igual que los rubros y los roles: sin al
            // menos una, un prestador no puede publicar (RN-S1-05 exige una zona de
            // cobertura) y la busqueda por cercania no tiene con que comparar.
            //
            // Son 16 sectores, no 256. OpenStreetMap conoce 256 lugares poblados dentro
            // de la comuna, hasta el ultimo condominio, y eso no sirve como selector:
            // nadie dice "atiendo en Condominio Nueva Milano". Estos son los sectores con
            // los que la gente de Temuco dice donde esta.
            //
            // PROCEDENCIA DE LAS COORDENADAS: cada punto es el nodo de OpenStreetMap con
            // ese nombre, consultado por Overpass el 7 de noviembre de 2026. No hay
            // ninguna coordenada inventada ni redondeada a mano, y eso importa: un
            // centroide mal puesto manda al prestador al barrio de al lado sin que nada
            // falle. Los datos de OSM son ODbL, que pide atribucion a
            // "OpenStreetMap contributors" donde se muestren.
            //
            // El administrador (A-06) gestiona las zonas, asi que esta lista es un punto
            // de partida y no una verdad fija.
            e.HasData(
                new Zona { Id = 1, Nombre = "Centro", Sector = "centro", CentroLatitud = -38.7400362, CentroLongitud = -72.5903905, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 2, Nombre = "Santa Rosa", Sector = "oriente", CentroLatitud = -38.7394601, CentroLongitud = -72.5757387, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 3, Nombre = "Parque Costanera", Sector = "oriente", CentroLatitud = -38.7090893, CentroLongitud = -72.5457341, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 4, Nombre = "Las Mariposas", Sector = "oriente", CentroLatitud = -38.6929012, CentroLongitud = -72.5470631, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 5, Nombre = "Pedro de Valdivia", Sector = "norte", CentroLatitud = -38.7221498, CentroLongitud = -72.6171431, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 6, Nombre = "Caupolicán", Sector = "norte", CentroLatitud = -38.7221214, CentroLongitud = -72.6223782, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 7, Nombre = "San José de Lanín", Sector = "norte", CentroLatitud = -38.720374, CentroLongitud = -72.6079935, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 8, Nombre = "Ñielol", Sector = "norte", CentroLatitud = -38.7287777, CentroLongitud = -72.5989384, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 9, Nombre = "El Bosque", Sector = "norte", CentroLatitud = -38.7227428, CentroLongitud = -72.6327342, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 10, Nombre = "Fundo El Carmen", Sector = "norte", CentroLatitud = -38.7143385, CentroLongitud = -72.653094, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 11, Nombre = "Amanecer", Sector = "poniente", CentroLatitud = -38.7563283, CentroLongitud = -72.6241415, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 12, Nombre = "Barrio Inglés", Sector = "poniente", CentroLatitud = -38.7375952, CentroLongitud = -72.6436021, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 13, Nombre = "Portal La Frontera", Sector = "poniente", CentroLatitud = -38.7480501, CentroLongitud = -72.6465207, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 14, Nombre = "Labranza", Sector = "poniente", CentroLatitud = -38.7665376, CentroLongitud = -72.7534177, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 15, Nombre = "Las Quilas", Sector = "sur", CentroLatitud = -38.7489399, CentroLongitud = -72.6069585, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco },
                new Zona { Id = 16, Nombre = "Pichicautín", Sector = "sur", CentroLatitud = -38.7455569, CentroLongitud = -72.5985394, Comuna = "Temuco", Tipo = TipoDeZona.Barrio, Region = "La Araucanía", PadreId = ZonasDeChile.IdTemuco });

            // Las 346 comunas de Chile (ver ZonasDeChile). La comuna es la unidad nacional; los barrios de arriba
            // son un nivel opcional dentro de la de Temuco.
            e.HasData(ZonasDeChile.Comunas);
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

    // ------------------------------------------------- MOD-05, solo lectura

    private static void ConfigurarBusqueda(ModelBuilder b)
    {
        // El resultado de la consulta de CU-08 no es una tabla ni una vista: es la forma
        // de una consulta. Se declara sin clave y sin tabla para que EF Core pueda
        // materializarla desde FromSqlInterpolated sin creer que existe en la base, y
        // para que ninguna migracion intente crearla.
        b.Entity<FilaDeBusqueda>(e =>
        {
            e.HasNoKey();
            e.ToView(null);
        });
        b.Entity<FilaDeTexto>(e =>
        {
            e.HasNoKey();
            e.ToView(null);
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

            // Los tres planes se siembran con el esquema, igual que los roles de
            // TBL-02. Sin el de prueba, IniciarPruebaAsync no tiene con que arrancar
            // y NADIE llegaria nunca a estar vigente: con RN-S3-09, eso seria una
            // busqueda permanentemente vacia. Los de pago son la salida de la
            // prueba: mensual es el precio normal; fundador son los primeros 20
            // cupos a precio de lanzamiento (el cupo vive en su propia tabla).
            e.HasData(
                new Plan
                {
                    Id = 1,
                    Codigo = "prueba",
                    Nombre = "Prueba",
                    Descripcion = "30 dias para publicar el perfil y aparecer en las busquedas.",
                    PrecioMensual = 0,
                    DiasPrueba = 30,
                    MaxServicios = 10,
                    MaxTarjetas = 1,
                    PermiteCredenciales = true,
                    Activo = true,
                },
                new Plan
                {
                    Id = 2,
                    Codigo = "mensual",
                    Nombre = "ZASS! Mensual",
                    Descripcion = "Publica tu perfil, apareces en las busquedas y recibes solicitudes de hora. 7990 pesos al mes; cancela cuando quieras.",
                    PrecioMensual = 7990,
                    DiasPrueba = 0,
                    MaxServicios = 10,
                    MaxTarjetas = 1,
                    PermiteCredenciales = true,
                    Activo = true,
                },
                new Plan
                {
                    Id = 3,
                    Codigo = "fundador",
                    Nombre = "ZASS! Fundador",
                    Descripcion = "Precio de lanzamiento para las primeras 20 personas: 5990 pesos al mes mientras mantengas tu suscripcion activa.",
                    PrecioMensual = 5990,
                    DiasPrueba = 0,
                    MaxServicios = 10,
                    MaxTarjetas = 1,
                    PermiteCredenciales = true,
                    Activo = true,
                });
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

        // A-09 · CC-08. El horario y la reserva del planificador, en la base y no en el proceso:
        // con varias replicas de la API, la memoria de una no sabe lo que hizo la otra.
        b.Entity<TrabajoDeFondo>(e =>
        {
            e.HasKey(x => x.Nombre);
            e.Property(x => x.Nombre).HasMaxLength(60);
            e.Property(x => x.UltimoResumen).HasMaxLength(300);
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
