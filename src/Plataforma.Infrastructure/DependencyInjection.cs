using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Guardar la base de datos en la carpeta temporal compartida de Windows
        var dbPath = Path.Combine(Path.GetTempPath(), "plataforma_demo.db");

        services.AddDbContext<PlataformaDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"), ServiceLifetime.Scoped);

        services.AddScoped<IEnvioRepositorio, EnvioRepositorio>();

        return services;
    }
}