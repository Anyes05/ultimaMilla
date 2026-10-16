using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Application;
using StackExchange.Redis;

namespace Plataforma.Infrastructure;

public sealed class RedisCacheLectura : ICacheLectura
{
    private readonly IDatabase _base;
    private long _aciertos;
    private long _fallos;

    public RedisCacheLectura(IConnectionMultiplexer multiplexer)
    {
        _base = multiplexer.GetDatabase();
    }

    public async Task<string?> LeerAsync(string clave, CancellationToken cancellationToken = default)
    {
        var valor = await _base.StringGetAsync(clave);
        if (valor.IsNullOrEmpty)
        {
            Interlocked.Increment(ref _fallos);
            return null;
        }

        Interlocked.Increment(ref _aciertos);
        return valor.ToString();
    }

    public Task GuardarAsync(string clave, string valor, TimeSpan vigencia, CancellationToken cancellationToken = default) =>
        _base.StringSetAsync(clave, valor, vigencia);

    public Task QuitarAsync(string clave, CancellationToken cancellationToken = default) =>
        _base.KeyDeleteAsync(clave);

    public EstadisticasCache Estadisticas()
    {
        var aciertos = Interlocked.Read(ref _aciertos);
        var fallos = Interlocked.Read(ref _fallos);
        return new EstadisticasCache(aciertos, fallos);
    }
}

public static class CacheRedis
{
    public static ConfigurationOptions? CrearOpciones(IConfiguration configuration)
    {
        var conexion = configuration["Redis:Configuration"];
        ConfigurationOptions opciones;

        if (!string.IsNullOrWhiteSpace(conexion))
        {
            opciones = ConfigurationOptions.Parse(conexion);
        }
        else
        {
            var host = configuration["Redis:Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            opciones = new ConfigurationOptions
            {
                Password = configuration["Redis:Password"]
            };
            opciones.EndPoints.Add(host, configuration.GetValue<int?>("Redis:Port") ?? 6379);
        }

        opciones.AbortOnConnectFail = false;
        opciones.ConnectTimeout = 3000;
        return opciones;
    }

    public static IServiceCollection AddCacheRedis(this IServiceCollection services, IConfiguration configuration)
    {
        var opciones = CrearOpciones(configuration);
        if (opciones is null)
        {
            services.AddSingleton<ICacheLectura, CacheLecturaMemoria>();
            return services;
        }

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(opciones));
        services.AddSingleton<ICacheLectura, RedisCacheLectura>();
        return services;
    }
}
