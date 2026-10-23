using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public class PlataformaDbContext : DbContext
{
    public PlataformaDbContext(DbContextOptions<PlataformaDbContext> options)
        : this(options, new OperadorActualFijo(null))
    {
    }

    public PlataformaDbContext(DbContextOptions<PlataformaDbContext> options, IOperadorActual operadorActual)
        : base(options)
    {
        OperadorIdFiltro = operadorActual.OperadorId;
    }

    /// <summary>
    /// Usado por los query filters. Null desactiva el filtro (seed, migraciones).
    /// </summary>
    public Guid? OperadorIdFiltro { get; }

    public DbSet<Envio> Envios => Set<Envio>();
    public DbSet<Operador> Operadores => Set<Operador>();
    public DbSet<Comercio> Comercios => Set<Comercio>();
    public DbSet<UsuarioPlataforma> Usuarios => Set<UsuarioPlataforma>();
    public DbSet<Repartidor> Repartidores => Set<Repartidor>();
    public DbSet<ReglaTarifaria> ReglasTarifarias => Set<ReglaTarifaria>();
    public DbSet<VigenciaTarifa> VigenciasTarifa => Set<VigenciaTarifa>();
    public DbSet<ZonaCobertura> ZonasCobertura => Set<ZonaCobertura>();
    public DbSet<ReglaOperativa> ReglasOperativas => Set<ReglaOperativa>();
    public DbSet<Destinatario> Destinatarios => Set<Destinatario>();
    public DbSet<Direccion> Direcciones => Set<Direccion>();
    public DbSet<HistorialEstado> HistorialEstados => Set<HistorialEstado>();
    public DbSet<IntentoEntrega> IntentosEntrega => Set<IntentoEntrega>();
    public DbSet<Novedad> Novedades => Set<Novedad>();
    public DbSet<Incidencia> Incidencias => Set<Incidencia>();
    public DbSet<Liquidacion> Liquidaciones => Set<Liquidacion>();
    public DbSet<SuscripcionWebhook> SuscripcionesWebhook => Set<SuscripcionWebhook>();
    public DbSet<AvisoComercio> AvisosComercio => Set<AvisoComercio>();
    public DbSet<Ruta> Rutas => Set<Ruta>();
    public DbSet<Parada> Paradas => Set<Parada>();
    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();
    public DbSet<PruebaDeEntrega> PruebasDeEntrega => Set<PruebaDeEntrega>();
    public DbSet<MensajeOutbox> MensajesOutbox => Set<MensajeOutbox>();
    public DbSet<DispositivoRepartidor> DispositivosRepartidor => Set<DispositivoRepartidor>();
    public DbSet<EventoSincronizacionProcesado> EventosSincronizacionProcesados => Set<EventoSincronizacionProcesado>();
    public DbSet<EventoAvisoProcesado> EventosAvisoProcesados => Set<EventoAvisoProcesado>();
    public DbSet<AvisoFallido> AvisosFallidos => Set<AvisoFallido>();
    public DbSet<ReceptorPruebaConfiguracion> ReceptoresPruebaConfiguracion => Set<ReceptorPruebaConfiguracion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Operador>().HasQueryFilter(operador =>
            OperadorIdFiltro == null || operador.Id == OperadorIdFiltro);

        AplicarFiltroOperador<Envio>(modelBuilder);
        AplicarFiltroOperador<Comercio>(modelBuilder);
        AplicarFiltroOperador<UsuarioPlataforma>(modelBuilder);
        AplicarFiltroOperador<Repartidor>(modelBuilder);
        AplicarFiltroOperador<ReglaTarifaria>(modelBuilder);
        AplicarFiltroOperador<VigenciaTarifa>(modelBuilder);
        AplicarFiltroOperador<ZonaCobertura>(modelBuilder);
        AplicarFiltroOperador<ReglaOperativa>(modelBuilder);
        AplicarFiltroOperador<Destinatario>(modelBuilder);
        AplicarFiltroOperador<Direccion>(modelBuilder);
        AplicarFiltroOperador<HistorialEstado>(modelBuilder);
        AplicarFiltroOperador<IntentoEntrega>(modelBuilder);
        AplicarFiltroOperador<Novedad>(modelBuilder);
        AplicarFiltroOperador<Incidencia>(modelBuilder);
        AplicarFiltroOperador<Liquidacion>(modelBuilder);
        AplicarFiltroOperador<SuscripcionWebhook>(modelBuilder);
        AplicarFiltroOperador<AvisoComercio>(modelBuilder);
        AplicarFiltroOperador<Ruta>(modelBuilder);
        AplicarFiltroOperador<Parada>(modelBuilder);
        AplicarFiltroOperador<Vehiculo>(modelBuilder);
        AplicarFiltroOperador<PruebaDeEntrega>(modelBuilder);
        AplicarFiltroOperador<MensajeOutbox>(modelBuilder);
        AplicarFiltroOperador<DispositivoRepartidor>(modelBuilder);
        AplicarFiltroOperador<EventoSincronizacionProcesado>(modelBuilder);
        AplicarFiltroOperador<EventoAvisoProcesado>(modelBuilder);
        AplicarFiltroOperador<AvisoFallido>(modelBuilder);
        AplicarFiltroOperador<ReceptorPruebaConfiguracion>(modelBuilder);

        modelBuilder.Entity<Envio>()
            .HasIndex(envio => envio.NumeroSeguimiento)
            .IsUnique();

        modelBuilder.Entity<Envio>()
            .HasIndex(envio => envio.TokenSeguimientoPublico)
            .IsUnique();

        modelBuilder.Entity<Operador>()
            .Property(operador => operador.ZonaHoraria)
            .HasMaxLength(64)
            .HasDefaultValue(Operador.ZonaHorariaUruguay);

        modelBuilder.Entity<MensajeOutbox>(entidad =>
        {
            entidad.HasKey(mensaje => mensaje.EventId);
            entidad.Property(mensaje => mensaje.Tipo).HasMaxLength(100);
            entidad.Property(mensaje => mensaje.Version).HasMaxLength(16);
            entidad.HasIndex(mensaje => new { mensaje.OperadorId, mensaje.PublicadoEn });
        });

        modelBuilder.Entity<DispositivoRepartidor>(entidad =>
        {
            entidad.HasKey(dispositivo => dispositivo.Id);
            entidad.Property(dispositivo => dispositivo.Token).HasMaxLength(512);
            entidad.Property(dispositivo => dispositivo.Plataforma).HasMaxLength(16);
            entidad.HasIndex(dispositivo => new { dispositivo.OperadorId, dispositivo.Token }).IsUnique();
        });

        modelBuilder.Entity<EventoSincronizacionProcesado>(entidad =>
        {
            entidad.HasKey(evento => evento.EventoId);
            entidad.HasIndex(evento => new { evento.OperadorId, evento.EnvioId });
        });

        modelBuilder.Entity<EventoAvisoProcesado>(entidad =>
        {
            entidad.HasKey(evento => evento.EventId);
            entidad.HasIndex(evento => new { evento.OperadorId, evento.EnvioId });
        });

        modelBuilder.Entity<AvisoFallido>(entidad =>
        {
            entidad.HasKey(aviso => aviso.Id);
            entidad.Property(aviso => aviso.Payload).IsRequired();
            entidad.Property(aviso => aviso.Error).HasMaxLength(2048);
            entidad.HasIndex(aviso => aviso.EventId).IsUnique();
            entidad.HasIndex(aviso => new { aviso.OperadorId, aviso.ResueltoEn });
        });

        modelBuilder.Entity<ReceptorPruebaConfiguracion>(entidad =>
        {
            entidad.HasKey(configuracion => configuracion.OperadorId);
        });
    }

    private void AplicarFiltroOperador<TEntidad>(ModelBuilder modelBuilder)
        where TEntidad : class
    {
        modelBuilder.Entity<TEntidad>().HasQueryFilter(entidad =>
            OperadorIdFiltro == null ||
            EF.Property<Guid>(entidad, nameof(Envio.OperadorId)) == OperadorIdFiltro);
    }
}
