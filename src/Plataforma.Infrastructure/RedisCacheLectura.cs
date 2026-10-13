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
    public static IServiceCollection AddCacheRedis(this IServiceCollection services, IConfiguration configuration)
    {
        var conexion = configuration["Redis:Configuration"]
            ?? throw new InvalidOperationException("Defina Redis__Configuration.");

        var opciones = ConfigurationOptions.Parse(conexion);
        opciones.AbortOnConnectFail = false;
        opciones.ConnectTimeout = 3000;

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(opciones));
        services.AddSingleton<ICacheLectura, RedisCacheLectura>();
        return services;
    }
}
