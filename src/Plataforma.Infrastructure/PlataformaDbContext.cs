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
    }

    private void AplicarFiltroOperador<TEntidad>(ModelBuilder modelBuilder)
        where TEntidad : class
    {
        modelBuilder.Entity<TEntidad>().HasQueryFilter(entidad =>
            OperadorIdFiltro == null ||
            EF.Property<Guid>(entidad, nameof(Envio.OperadorId)) == OperadorIdFiltro);
    }
}
