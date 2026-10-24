using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Plataforma.Avisos;

public static class DependencyInjectionAvisos
{
    /// <summary>
    /// Registra el módulo de avisos. Usa la misma ConnectionStrings:Default que el resto
    /// (las tablas viven en el esquema "avisos") y la misma sección RabbitMq.
    /// </summary>
    public static IServiceCollection AddAvisos(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpcionesRabbitMq>(configuration.GetSection(OpcionesRabbitMq.Seccion));
        services.Configure<OpcionesAvisos>(configuration.GetSection(OpcionesAvisos.Seccion));

        var connectionString = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(connectionString))
            services.AddDbContext<AvisosDbContext>(opciones => AvisosDbContext.Configurar(opciones, connectionString));

        services.AddSingleton<PublicadorRabbitAvisos>();
        services.AddSingleton<IPublicadorAvisos>(proveedor => proveedor.GetRequiredService<PublicadorRabbitAvisos>());
        services.AddScoped<ServicioAvisos>();
        return services;
    }

    /// <summary>Aplica las migraciones del esquema "avisos" (job de migración y arranque del worker).</summary>
    public static async Task MigrarAvisosAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AvisosDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
