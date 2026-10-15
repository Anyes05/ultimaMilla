using System.Text.Json;
using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Lee la configuración visible del operador desde Redis (o DB si hay miss).</summary>
public sealed class ConfiguracionOperador
{
    private readonly IOperadorRepositorio _operadores;
    private readonly ICacheLectura _cache;

    public ConfiguracionOperador(IOperadorRepositorio operadores, ICacheLectura cache)
    {
        _operadores = operadores;
        _cache = cache;
    }

    public async Task<OperadorVisible> ObtenerAsync(Guid operadorId, CancellationToken cancellationToken = default)
    {
        var clave = ClavesCache.Operador(operadorId);
        var cacheado = await _cache.LeerAsync(clave, cancellationToken);
        if (cacheado is not null)
        {
            return JsonSerializer.Deserialize<OperadorVisible>(cacheado, ObtenerRutasDeHoy.OpcionesJson)
                ?? throw new InvalidOperationException("La configuración cacheada del operador es inválida.");
        }

        var operador = await _operadores.ObtenerAsync(operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El operador de la sesión no existe.");

        var visible = OperadorVisible.Desde(operador);
        await _cache.GuardarAsync(
            clave,
            JsonSerializer.Serialize(visible, ObtenerRutasDeHoy.OpcionesJson),
            TimeSpan.FromHours(24),
            cancellationToken);
        return visible;
    }

    public async Task<Operador> ObtenerEntidadAsync(Guid operadorId, CancellationToken cancellationToken = default)
    {
        // Asegura que la config visible quede en caché; la entidad viene de DB.
        _ = await ObtenerAsync(operadorId, cancellationToken);
        return await _operadores.ObtenerAsync(operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El operador de la sesión no existe.");
    }
}
