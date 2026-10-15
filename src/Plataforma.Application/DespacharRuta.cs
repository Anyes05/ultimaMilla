using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>
/// Crea una <see cref="Ruta"/> con <see cref="Parada"/>s y asigna los envíos (EnDeposito → AsignadoARuta).
/// </summary>
public sealed class DespacharRuta
{
    private readonly IRutaRepositorio _rutas;
    private readonly IEnvioRepositorio _envios;
    private readonly IRepartidorRepositorio _repartidores;
    private readonly ICacheLectura _cache;

    public DespacharRuta(
        IRutaRepositorio rutas,
        IEnvioRepositorio envios,
        IRepartidorRepositorio repartidores,
        ICacheLectura cache)
    {
        _rutas = rutas;
        _envios = envios;
        _repartidores = repartidores;
        _cache = cache;
    }

    public async Task<Ruta> EjecutarAsync(
        Guid operadorId,
        Guid repartidorId,
        DateOnly fecha,
        IReadOnlyList<Guid> envioIds,
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        if (envioIds is null || envioIds.Count == 0)
            throw new ArgumentException("La ruta necesita al menos un envío.", nameof(envioIds));

        if (envioIds.Distinct().Count() != envioIds.Count)
            throw new ArgumentException("Hay envíos duplicados en el despacho.", nameof(envioIds));

        var repartidor = await _repartidores.ObtenerAsync(repartidorId, operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El repartidor no existe para ese operador.");

        if (!repartidor.Activo)
            throw new InvalidOperationException("El repartidor no está activo.");

        var ruta = Ruta.Programar(operadorId, repartidorId, fecha);
        var paradas = new List<Parada>(envioIds.Count);
        var orden = 1;

        foreach (var envioId in envioIds)
        {
            var envio = await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken)
                ?? throw new InvalidOperationException($"El envío {envioId} no existe para ese operador.");

            if (envio.RutaId is not null)
                throw new InvalidOperationException($"El envío {envio.NumeroSeguimiento} ya está en una ruta.");

            envio.AsignarARuta(ruta.Id);
            paradas.Add(Parada.Programar(Guid.NewGuid(), operadorId, ruta.Id, envioId, orden++));
            await CacheSeguimiento.PublicarAsync(_cache, envio, cancellationToken);
        }

        await _rutas.GuardarDespachoAsync(ruta, paradas, cancellationToken);
        await _envios.GuardarAsync(cancellationToken);
        await _cache.QuitarAsync(ObtenerRutasDeHoy.Clave(operadorId, repartidorId, fecha), cancellationToken);

        return ruta;
    }
}
