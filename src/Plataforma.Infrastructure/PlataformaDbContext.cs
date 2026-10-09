using Microsoft.EntityFrameworkCore;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public class PlataformaDbContext : DbContext
{
    public PlataformaDbContext(DbContextOptions<PlataformaDbContext> options) : base(options)
    {
        Database.EnsureCreated(); // Crea el archivo plataforma.db automáticamente al iniciar
    }

    public DbSet<Envio> Envios => Set<Envio>();
}