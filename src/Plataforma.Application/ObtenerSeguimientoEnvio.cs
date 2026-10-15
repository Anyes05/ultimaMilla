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
        if (envio is null)
            return null;

        var visible = SeguimientoVisible.Desde(envio);
        var json = JsonSerializer.Serialize(visible, ObtenerRutasDeHoy.OpcionesJson);
        await _cache.GuardarAsync(clave, json, TimeSpan.FromHours(48), cancellationToken);
        await _cache.GuardarAsync(
            ClavesCache.SeguimientoPorNumero(envio.NumeroSeguimiento),
            json,
            TimeSpan.FromHours(48),
            cancellationToken);
        return visible;
    }
}
