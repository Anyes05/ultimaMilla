using Microsoft.EntityFrameworkCore;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public class PlataformaDbContext : DbContext
{
    public PlataformaDbContext(DbContextOptions<PlataformaDbContext> options) : base(options)
    {
    }

    public DbSet<Envio> Envios => Set<Envio>();
}