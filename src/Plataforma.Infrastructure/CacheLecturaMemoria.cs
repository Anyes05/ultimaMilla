using System.Collections.Concurrent;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

/// <summary>Caché en memoria para tests o cuando Redis no está configurado.</summary>
public sealed class CacheLecturaMemoria : ICacheLectura
{
    private readonly ConcurrentDictionary<string, (string Valor, DateTimeOffset Expira)> _valores = new();
    private long _aciertos;
    private long _fallos;

    public Task<string?> LeerAsync(string clave, CancellationToken cancellationToken = default)
    {
        if (_valores.TryGetValue(clave, out var entrada) && entrada.Expira > DateTimeOffset.UtcNow)
        {
            Interlocked.Increment(ref _aciertos);
            return Task.FromResult<string?>(entrada.Valor);
        }

        _valores.TryRemove(clave, out _);
        Interlocked.Increment(ref _fallos);
        return Task.FromResult<string?>(null);
    }

    public Task GuardarAsync(string clave, string valor, TimeSpan vigencia, CancellationToken cancellationToken = default)
    {
        _valores[clave] = (valor, DateTimeOffset.UtcNow.Add(vigencia));
        return Task.CompletedTask;
    }

    public Task QuitarAsync(string clave, CancellationToken cancellationToken = default)
    {
        _valores.TryRemove(clave, out _);
        return Task.CompletedTask;
    }

    public EstadisticasCache Estadisticas()
    {
        var aciertos = Interlocked.Read(ref _aciertos);
        var fallos = Interlocked.Read(ref _fallos);
        return new EstadisticasCache(aciertos, fallos);
    }
}
