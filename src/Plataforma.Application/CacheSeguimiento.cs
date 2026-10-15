using System.Text.Json;
using Plataforma.Domain;

namespace Plataforma.Application;

public static class CacheSeguimiento
{
    public static Task PublicarAsync(ICacheLectura cache, Envio envio, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(SeguimientoVisible.Desde(envio), ObtenerRutasDeHoy.OpcionesJson);
        return Task.WhenAll(
            cache.GuardarAsync(ClavesCache.Seguimiento(envio.TokenSeguimientoPublico), json, TimeSpan.FromHours(48), cancellationToken),
            cache.GuardarAsync(ClavesCache.SeguimientoPorNumero(envio.NumeroSeguimiento), json, TimeSpan.FromHours(48), cancellationToken));
    }
}
