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
    private readonly IBultoRepositorio _bultos;
    private readonly ICoberturaRepositorio _cobertura;
    private readonly RegistrarOutboxEstado _outbox;
    private readonly PublicarOutboxPendientes _publicar;
    private readonly ICacheLectura _cache;

    public DespacharRuta(
        IRutaRepositorio rutas,
        IEnvioRepositorio envios,
        IRepartidorRepositorio repartidores,
        IBultoRepositorio bultos,
        ICoberturaRepositorio cobertura,
        RegistrarOutboxEstado outbox,
        PublicarOutboxPendientes publicar,
        ICacheLectura cache)
    {
        _rutas = rutas;
        _envios = envios;
        _repartidores = repartidores;
        _bultos = bultos;
        _cobertura = cobertura;
        _outbox = outbox;
        _publicar = publicar;
        _cache = cache;
    }

    public async Task<Ruta> EjecutarAsync(
        Guid operadorId,
        Guid repartidorId,
        DateOnly fecha,
        IReadOnlyList<Guid> envioIds,
        Guid? vehiculoId = null,
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

        // Capacidad: solo bultos disponibles para nueva ruta (RF 34 / capacidad variable).
        var bultosPorEnvio = new Dictionary<Guid, IReadOnlyList<Bulto>>();
        foreach (var envioIdCap in envioIds)
        {
            var todos = await _bultos.ListarPorEnvioAsync(operadorId, envioIdCap, cancellationToken);
            bultosPorEnvio[envioIdCap] = todos;
        }

        if (vehiculoId is Guid vid)
        {
            var vehiculo = await _cobertura.ObtenerVehiculoAsync(operadorId, vid, cancellationToken)
                ?? throw new InvalidOperationException("El vehículo no existe para ese operador.");
            if (!vehiculo.Activo)
                throw new InvalidOperationException("El vehículo no está activo.");
            if (vehiculo.MaximoParadas > 0 && envioIds.Count > vehiculo.MaximoParadas)
                throw new InvalidOperationException(
                    $"El vehículo admite como máximo {vehiculo.MaximoParadas} paradas.");

            decimal peso = 0;
            decimal volumen = 0;
            foreach (var envioIdCap in envioIds)
            {
                var despachables = bultosPorEnvio[envioIdCap]
                    .Where(b => b.DisponibleParaNuevaRuta)
                    .ToList();
                // Sin bultos (flujo v1): no suma capacidad; con bultos, solo los pendientes.
                if (bultosPorEnvio[envioIdCap].Count > 0 && despachables.Count == 0)
                    throw new InvalidOperationException(
                        $"El envío {envioIdCap} no tiene bultos pendientes para una nueva ruta.");

                peso += despachables.Sum(b => b.PesoKg);
                volumen += despachables.Sum(b => b.AltoCm * b.AnchoCm * b.LargoCm);
            }

            if (vehiculo.CapacidadPesoKg > 0 && peso > vehiculo.CapacidadPesoKg)
                throw new InvalidOperationException(
                    $"El peso de la ruta ({peso} kg) supera la capacidad del vehículo ({vehiculo.CapacidadPesoKg} kg).");

            if (vehiculo.CapacidadVolumenCm3 > 0 && volumen > vehiculo.CapacidadVolumenCm3)
                throw new InvalidOperationException(
                    $"El volumen de la ruta ({volumen} cm³) supera la capacidad del vehículo ({vehiculo.CapacidadVolumenCm3} cm³).");
        }

        var ruta = Ruta.Programar(operadorId, repartidorId, fecha);
        var paradas = new List<Parada>(envioIds.Count);
        var eventIds = new List<Guid>();
        var orden = 1;

        foreach (var envioId in envioIds)
        {
            var envio = await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken)
                ?? throw new InvalidOperationException($"El envío {envioId} no existe para ese operador.");

            if (envio.RutaId is not null)
                throw new InvalidOperationException($"El envío {envio.NumeroSeguimiento} ya está en una ruta.");

            // Rutas mixtas: depósito, reprogramados y parciales con bultos pendientes (RF 34).
            if (envio.Estado is not (EstadoEnvio.EnDeposito or EstadoEnvio.Reprogramado or EstadoEnvio.EntregadoParcialmente))
                throw new InvalidOperationException(
                    $"El envío {envio.NumeroSeguimiento} no está disponible para despacho ({envio.Estado}).");

            if (bultosPorEnvio.TryGetValue(envioId, out var bultosEnvio) && bultosEnvio.Count > 0
                && bultosEnvio.All(b => !b.DisponibleParaNuevaRuta))
                throw new InvalidOperationException(
                    $"El envío {envio.NumeroSeguimiento} no tiene bultos pendientes para despachar.");

            var anterior = envio.Estado;
            envio.AsignarARuta(ruta.Id);
            var eventId = await _outbox.EjecutarAsync(
                operadorId, envio.Id, anterior, envio.Estado, cancellationToken, responsable: "despacho");
            if (eventId is Guid id)
                eventIds.Add(id);
            paradas.Add(Parada.Programar(Guid.NewGuid(), operadorId, ruta.Id, envioId, orden++));
            await CacheSeguimiento.PublicarAsync(_cache, envio, cancellationToken);
        }

        await _rutas.GuardarDespachoAsync(ruta, paradas, cancellationToken);
        await _envios.GuardarAsync(cancellationToken);
        foreach (var eventId in eventIds)
            await _publicar.EjecutarUnoAsync(eventId, cancellationToken);
        await _cache.QuitarAsync(ObtenerRutasDeHoy.Clave(operadorId, repartidorId, fecha), cancellationToken);

        return ruta;
    }
}
