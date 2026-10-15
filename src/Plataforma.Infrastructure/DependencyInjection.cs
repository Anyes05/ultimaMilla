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

        services.AddHttpContextAccessor();
        services.AddScoped<IOperadorActual, OperadorActualHttp>();
        services.AddScoped<OperadorSaveChangesInterceptor>();

        services.AddDbContext<PlataformaDbContext>((proveedor, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(proveedor.GetRequiredService<OperadorSaveChangesInterceptor>());
        });

        services.AddCacheRedis(configuration);

        services.AddScoped<IEnvioRepositorio, EnvioRepositorio>();
        services.AddScoped<IUsuarioRepositorio, UsuarioRepositorio>();
        services.AddScoped<IOperadorRepositorio, OperadorRepositorio>();
        services.AddScoped<IRutaRepositorio, RutaRepositorio>();
        services.AddScoped<IRepartidorRepositorio, RepartidorRepositorio>();
        services.AddScoped<IVerificadorClave, HashClave>();
        services.AddScoped<IEmisorToken, EmisorTokenJwt>();
        services.AddScoped<ConfiguracionOperador>();
        services.AddScoped<CambiarEstadoEnvio>();
        services.AddScoped<ITransicionEnvio>(proveedor => proveedor.GetRequiredService<CambiarEstadoEnvio>());
        services.AddScoped<IniciarSesion>();
        services.AddScoped<IniciarSesionRepartidor>();
        services.AddScoped<ListarEnvios>();
        services.AddScoped<DespacharRuta>();
        services.AddScoped<ObtenerRutasDeHoy>();
        services.AddScoped<SincronizarEntregas>();
        services.AddScoped<ObtenerSeguimientoEnvio>();

        return services;
    }

    public static async Task MigrarBaseDeDatosAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        await context.Database.MigrateAsync();
        await SemillaDatos.AplicarAsync(context);
    }
}
