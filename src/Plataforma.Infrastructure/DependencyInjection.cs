using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("La cadena de conexion 'Default' es requerida.");

        services.AddDbContext<PlataformaDbContext>(options =>
            options.UseNpgsql(connectionString), ServiceLifetime.Scoped);

        services.AddScoped<IEnvioRepositorio, EnvioRepositorio>();
        services.AddScoped<IUsuarioRepositorio, UsuarioRepositorio>();
        services.AddScoped<IOperadorRepositorio, OperadorRepositorio>();
        services.AddScoped<IRutaRepositorio, RutaRepositorio>();
        services.AddScoped<IVerificadorClave, HashClave>();
        services.AddScoped<IEmisorToken, EmisorTokenJwt>();
        services.AddScoped<IniciarSesion>();
        services.AddScoped<ListarEnvios>();

        return services;
    }

    public static async Task MigrarBaseDeDatosAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        await context.Database.MigrateAsync();
    }
}