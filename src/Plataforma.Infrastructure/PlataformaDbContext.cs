using Microsoft.EntityFrameworkCore;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public class PlataformaDbContext : DbContext
{
    public PlataformaDbContext(DbContextOptions<PlataformaDbContext> options) : base(options)
    {
    }

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
}