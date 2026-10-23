using System.Text.Json;
using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class ObtenerSeguimientoEnvio
{
    private readonly IEnvioRepositorio _envios;
    private readonly ICacheLectura _cache;

    public ObtenerSeguimientoEnvio(IEnvioRepositorio envios, ICacheLectura cache)
    {
        _envios = envios;
        _cache = cache;
    }

    public async Task<SeguimientoVisible?> PorTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var clave = ClavesCache.Seguimiento(token.Trim());
        var cacheado = await _cache.LeerAsync(clave, cancellationToken);
        if (cacheado is not null)
            return JsonSerializer.Deserialize<SeguimientoVisible>(cacheado, ObtenerRutasDeHoy.OpcionesJson);

        var envio = await _envios.ObtenerPorTokenAsync(token.Trim(), cancellationToken);
        return envio is null ? null : await PublicarYDevolverAsync(envio, cancellationToken);
    }

    public async Task<SeguimientoVisible?> PorNumeroAsync(string numero, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(numero))
            return null;

        var clave = ClavesCache.SeguimientoPorNumero(numero.Trim());
        var cacheado = await _cache.LeerAsync(clave, cancellationToken);
        if (cacheado is not null)
            return JsonSerializer.Deserialize<SeguimientoVisible>(cacheado, ObtenerRutasDeHoy.OpcionesJson);

        var envio = await _envios.ObtenerPorNumeroAsync(numero.Trim(), cancellationToken);
        return envio is null ? null : await PublicarYDevolverAsync(envio, cancellationToken);
    }

    private async Task<SeguimientoVisible> PublicarYDevolverAsync(Envio envio, CancellationToken cancellationToken)
    {
        var visible = SeguimientoVisible.Desde(envio);
        await CacheSeguimiento.PublicarAsync(_cache, envio, cancellationToken);
        return visible;
    }
}
