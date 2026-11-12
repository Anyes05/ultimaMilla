using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public class PlataformaDbContext : IdentityDbContext<UsuarioIdentity, IdentityRole<Guid>, Guid>
{
    public PlataformaDbContext(
        DbContextOptions<PlataformaDbContext> options,
        IOperadorActual? operadorActual = null)
        : base(options)
    {
        OperadorIdFiltro = operadorActual?.OperadorId;
    }

    /// <summary>
    /// Usado por los query filters. Null desactiva el filtro (seed, migraciones).
    /// </summary>
    public Guid? OperadorIdFiltro { get; }

    public DbSet<Envio> Envios => Set<Envio>();
    public DbSet<Bulto> Bultos => Set<Bulto>();
    public DbSet<Operador> Operadores => Set<Operador>();
    public DbSet<Comercio> Comercios => Set<Comercio>();
    public DbSet<UsuarioPlataforma> Usuarios => Set<UsuarioPlataforma>();
    public DbSet<Repartidor> Repartidores => Set<Repartidor>();
    public DbSet<ReglaTarifaria> ReglasTarifarias => Set<ReglaTarifaria>();
    public DbSet<VigenciaTarifa> VigenciasTarifa => Set<VigenciaTarifa>();
    public DbSet<ZonaCobertura> ZonasCobertura => Set<ZonaCobertura>();
    public DbSet<FranjaHoraria> FranjasHorarias => Set<FranjaHoraria>();
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
    public DbSet<BanderaOperador> BanderasOperador => Set<BanderaOperador>();
    public DbSet<CambioBandera> CambiosBandera => Set<CambioBandera>();
    public DbSet<SolicitudRetiro> SolicitudesRetiro => Set<SolicitudRetiro>();
    public DbSet<RecepcionEnvio> RecepcionesEnvio => Set<RecepcionEnvio>();
    public DbSet<PosicionRepartidor> PosicionesRepartidor => Set<PosicionRepartidor>();
    public DbSet<RecargoZonaTarifa> RecargosZonaTarifa => Set<RecargoZonaTarifa>();
    public DbSet<BonificacionTarifa> BonificacionesTarifa => Set<BonificacionTarifa>();
    public DbSet<CierreDiario> CierresDiarios => Set<CierreDiario>();
    public DbSet<ComprobanteRetiro> ComprobantesRetiro => Set<ComprobanteRetiro>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Operador>().HasQueryFilter(operador =>
            OperadorIdFiltro == null || operador.Id == OperadorIdFiltro);

        AplicarFiltroOperador<Envio>(modelBuilder);
        AplicarFiltroOperador<Bulto>(modelBuilder);
        AplicarFiltroOperador<Comercio>(modelBuilder);
        AplicarFiltroOperador<UsuarioPlataforma>(modelBuilder);
        AplicarFiltroOperador<Repartidor>(modelBuilder);
        AplicarFiltroOperador<ReglaTarifaria>(modelBuilder);
        AplicarFiltroOperador<VigenciaTarifa>(modelBuilder);
        AplicarFiltroOperador<ZonaCobertura>(modelBuilder);
        AplicarFiltroOperador<FranjaHoraria>(modelBuilder);
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
        AplicarFiltroOperador<BanderaOperador>(modelBuilder);
        AplicarFiltroOperador<CambioBandera>(modelBuilder);
        AplicarFiltroOperador<SolicitudRetiro>(modelBuilder);
        AplicarFiltroOperador<RecepcionEnvio>(modelBuilder);
        AplicarFiltroOperador<PosicionRepartidor>(modelBuilder);
        AplicarFiltroOperador<RecargoZonaTarifa>(modelBuilder);
        AplicarFiltroOperador<BonificacionTarifa>(modelBuilder);
        AplicarFiltroOperador<CierreDiario>(modelBuilder);
        AplicarFiltroOperador<ComprobanteRetiro>(modelBuilder);

        modelBuilder.Entity<Envio>(entidad =>
        {
            entidad.HasIndex(envio => envio.NumeroSeguimiento).IsUnique();
            entidad.HasIndex(envio => envio.TokenSeguimientoPublico).IsUnique();
            entidad.Property(envio => envio.Version).IsConcurrencyToken();
            entidad.Property(envio => envio.ClaveExterna).HasMaxLength(120);
            entidad.HasIndex(envio => new { envio.OperadorId, envio.ComercioId, envio.ClaveExterna })
                .IsUnique()
                .HasFilter("\"ClaveExterna\" IS NOT NULL");
        });

        modelBuilder.Entity<Bulto>(entidad =>
        {
            entidad.HasKey(bulto => bulto.Id);
            entidad.Property(bulto => bulto.Codigo).HasMaxLength(64);
            entidad.Property(bulto => bulto.MotivoNoEntrega).HasMaxLength(200);
            entidad.HasIndex(bulto => new { bulto.OperadorId, bulto.EnvioId, bulto.Codigo }).IsUnique();
            entidad.HasIndex(bulto => new { bulto.OperadorId, bulto.Estado });
        });

        modelBuilder.Entity<PruebaDeEntrega>(entidad =>
        {
            entidad.HasIndex(prueba => new { prueba.OperadorId, prueba.BultoId });
        });

        modelBuilder.Entity<ReglaOperativa>(entidad =>
        {
            entidad.Property(regla => regla.AdmiteEntregaParcial).HasDefaultValue(false);
            entidad.Property(regla => regla.MotivosNoEntrega)
                .IsRequired()
                .HasColumnType("text[]")
                .HasDefaultValueSql("ARRAY['Ausente','Destinatario ausente','Rechazo del destinatario','Domicilio no encontrado','Bulto danado']::text[]");
            entidad.Property(regla => regla.PoliticaBultosPendientes)
                .HasConversion<int>()
                .HasDefaultValue(PoliticaBultosPendientes.NuevaVisitaSinRecargo);
        });

        modelBuilder.Entity<Operador>()
            .Property(operador => operador.ZonaHoraria)
            .HasMaxLength(64)
            .HasDefaultValue(Operador.ZonaHorariaUruguay);

        modelBuilder.Entity<MensajeOutbox>(entidad =>
        {
            entidad.HasKey(mensaje => mensaje.EventId);
            entidad.Property(mensaje => mensaje.Tipo).HasMaxLength(100);
            entidad.Property(mensaje => mensaje.Version).HasMaxLength(16);
            entidad.Property(mensaje => mensaje.TraceParent).HasMaxLength(128);
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

        modelBuilder.Entity<HistorialEstado>(entidad =>
        {
            entidad.Property(h => h.Responsable).HasMaxLength(120).HasDefaultValue(string.Empty);
            entidad.Property(h => h.Motivo).HasMaxLength(500);
        });

        modelBuilder.Entity<BanderaOperador>(entidad =>
        {
            entidad.HasKey(b => new { b.OperadorId, b.Clave });
            entidad.Property(b => b.Clave).HasMaxLength(64);
        });

        modelBuilder.Entity<CambioBandera>(entidad =>
        {
            entidad.HasKey(c => c.Id);
            entidad.Property(c => c.Clave).HasMaxLength(64);
            entidad.Property(c => c.Usuario).HasMaxLength(120);
            entidad.HasIndex(c => new { c.OperadorId, c.Fecha });
        });

        modelBuilder.Entity<FranjaHoraria>(entidad =>
        {
            entidad.HasKey(f => f.Id);
            entidad.Property(f => f.Nombre).HasMaxLength(80);
        });

        modelBuilder.Entity<Vehiculo>(entidad =>
        {
            entidad.Property(v => v.CapacidadPesoKg).HasDefaultValue(0m);
            entidad.Property(v => v.CapacidadVolumenCm3).HasDefaultValue(0m);
            entidad.Property(v => v.MaximoParadas).HasDefaultValue(0);
        });

        modelBuilder.Entity<SolicitudRetiro>(entidad =>
        {
            entidad.HasKey(r => r.Id);
            entidad.Property(r => r.Direccion).HasMaxLength(300);
            entidad.Property(r => r.Contacto).HasMaxLength(120);
            entidad.Property(r => r.MotivoNoRetiro).HasMaxLength(300).HasDefaultValue(string.Empty);
            entidad.Property(r => r.Motivo).HasMaxLength(200).HasDefaultValue(string.Empty);
            entidad.Property(r => r.CantidadBultos).HasDefaultValue(1);
            entidad.HasIndex(r => new { r.OperadorId, r.Estado });
        });

        modelBuilder.Entity<RecepcionEnvio>(entidad =>
        {
            entidad.HasKey(r => r.Id);
            entidad.Property(r => r.Usuario).HasMaxLength(120);
            entidad.Property(r => r.BultosFaltantes).HasMaxLength(4000).HasDefaultValue(string.Empty);
            entidad.HasIndex(r => new { r.OperadorId, r.EnvioId });
        });

        modelBuilder.Entity<PosicionRepartidor>(entidad =>
        {
            entidad.HasKey(p => p.Id);
            entidad.Property(p => p.Latitud).HasPrecision(9, 6);
            entidad.Property(p => p.Longitud).HasPrecision(9, 6);
            entidad.Property(p => p.PrecisionMetros).HasPrecision(9, 2);
            // La consulta del Tablero busca la última de cada repartidor del operador en el día.
            entidad.HasIndex(p => new { p.OperadorId, p.RepartidorId, p.FechaUtc });
        });

        // RF 3: condiciones de cada versión de la tarifa; se versionan con la regla.
        modelBuilder.Entity<RecargoZonaTarifa>(entidad =>
        {
            entidad.HasKey(r => r.Id);
            entidad.Property(r => r.Recargo).HasPrecision(18, 2);
            entidad.HasIndex(r => new { r.ReglaTarifariaId, r.ZonaId }).IsUnique();
        });

        modelBuilder.Entity<BonificacionTarifa>(entidad =>
        {
            entidad.HasKey(b => b.Id);
            entidad.Property(b => b.Nombre).HasMaxLength(80);
            entidad.Property(b => b.Porcentaje).HasPrecision(5, 2);
            entidad.HasIndex(b => b.ReglaTarifariaId);
        });

        // Lo escribe el worker con SQL (CierreDiarioOperacion): la clave única evita dos cierres del mismo día.
        // RF 40: evidencia del retiro desde la app; el eventoId del teléfono la hace idempotente.
        modelBuilder.Entity<ComprobanteRetiro>(entidad =>
        {
            entidad.HasKey(c => c.Id);
            entidad.HasIndex(c => new { c.OperadorId, c.EventoId }).IsUnique();
            entidad.HasIndex(c => c.RetiroId);
            entidad.Property(c => c.CodigosBultos).HasMaxLength(10_200);
            entidad.Property(c => c.NombreEntrega).HasMaxLength(100);
            entidad.Property(c => c.Motivo).HasMaxLength(300);
            entidad.Property(c => c.Usuario).HasMaxLength(200);
            entidad.Property(c => c.Latitud).HasPrecision(9, 6);
            entidad.Property(c => c.Longitud).HasPrecision(9, 6);
            entidad.Ignore(c => c.Codigos);
        });

        modelBuilder.Entity<CierreDiario>(entidad =>
        {
            entidad.HasKey(c => c.Id);
            entidad.HasIndex(c => new { c.OperadorId, c.Dia }).IsUnique();
            entidad.Ignore(c => c.PorcentajeATiempo);
            entidad.Ignore(c => c.PorcentajeEfectividad);
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
