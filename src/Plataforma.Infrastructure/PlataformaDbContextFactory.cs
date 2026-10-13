using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Plataforma.Infrastructure;

public class PlataformaDbContextFactory : IDesignTimeDbContextFactory<PlataformaDbContext>
{
    public PlataformaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? throw new InvalidOperationException(
                "Defina ConnectionStrings__Default para crear o actualizar migraciones de Entity Framework Core.");

        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PlataformaDbContext(options);
    }
}