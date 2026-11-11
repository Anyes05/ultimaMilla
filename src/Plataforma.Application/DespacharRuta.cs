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
    private readonly IHistorialEstadoRepositorio? _historial;
    private readonly IReglaOperativaRepositorio? _reglas;

    public DespacharRuta(
        IRutaRepositorio rutas,
        IEnvioRepositorio envios,
        IRepartidorRepositorio repartidores,
        IBultoRepositorio bultos,
        ICoberturaRepositorio cobertura,
        RegistrarOutboxEstado outbox,
        PublicarOutboxPendientes publicar,
        ICacheLectura cache,
        IHistorialEstadoRepositorio? historial = null,
        IReglaOperativaRepositorio? reglas = null)
    {
        _historial = historial;
        _reglas = reglas;
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

        await ValidarFranjasAsync(operadorId, fecha, envioIds, cancellationToken);
        await ValidarPlazoEntreIntentosAsync(operadorId, envioIds, cancellationToken);

        var ruta = Ruta.Programar(operadorId, repartidorId, fecha);
        var paradas = new List<Parada>(envioIds.Count);
        var eventIds = new List<Guid>();
        var orden = 1;

        // RF 16: el orden de visita lo decide el despacho (ver OrdenDeParadas) y la app lo recorre tal cual.
        foreach (var envioId in await OrdenarAsync(operadorId, envioIds, cancellationToken))
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

    /// <summary>
    /// RF 16, criterio de orden de las paradas. Sin coordenadas de las direcciones no hay optimización geográfica:
    /// 1) urgentes primero (tienen el plazo más corto); 2) el resto agrupado por zona (por nombre, las sin zona al
    /// final), para no cruzar la ciudad varias veces; 3) dentro de cada grupo, el orden en que se eligieron.
    /// La franja no entra porque una ruta lleva envíos de una sola franja (RF 14).
    /// </summary>
    public static IReadOnlyList<Envio> OrdenDeParadas(
        IReadOnlyList<Envio> enviosEnOrdenElegido,
        IReadOnlyDictionary<Guid, string> nombresDeZona) =>
        enviosEnOrdenElegido
            .Select((envio, posicion) => (Envio: envio, Posicion: posicion, Zona: ZonaDe(envio, nombresDeZona)))
            .OrderBy(item => item.Envio.Urgente ? 0 : 1)
            .ThenBy(item => item.Zona is null ? 1 : 0)
            .ThenBy(item => item.Zona ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Posicion)
            .Select(item => item.Envio)
            .ToList();

    private static string? ZonaDe(Envio envio, IReadOnlyDictionary<Guid, string> nombresDeZona) =>
        envio.ZonaId is Guid zona && nombresDeZona.TryGetValue(zona, out var nombre) ? nombre : null;

    /// <summary>Ids en orden de visita; los que no existen quedan al final para que el despacho los rechace.</summary>
    private async Task<IReadOnlyList<Guid>> OrdenarAsync(Guid operadorId, IReadOnlyList<Guid> envioIds, CancellationToken cancellationToken)
    {
        var envios = new List<Envio>(envioIds.Count);
        foreach (var envioId in envioIds)
        {
            if (await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken) is { } envio)
                envios.Add(envio);
        }

        var zonas = (await _cobertura.ListarZonasAsync(operadorId, cancellationToken)).ToDictionary(zona => zona.Id, zona => zona.Nombre);
        var ordenados = OrdenDeParadas(envios, zonas).Select(envio => envio.Id).ToList();
        ordenados.AddRange(envioIds.Except(ordenados));
        return ordenados;
    }

    /// <summary>
    /// RF 4, plazo entre intentos: un envío que tuvo un intento fallido (no entregado o entregado en parte) no vuelve
    /// a salir hasta que pase el plazo de la regla vigente.
    /// </summary>
    private async Task ValidarPlazoEntreIntentosAsync(Guid operadorId, IReadOnlyList<Guid> envioIds, CancellationToken cancellationToken)
    {
        if (_historial is null || _reglas is null)
            return;

        var ahora = DateTime.UtcNow;
        var regla = await _reglas.ObtenerVigenteAsync(operadorId, ahora, cancellationToken);
        if (regla is null || regla.PlazoEntreIntentosHoras <= 0)
            return;

        var ultimos = (await _historial.ListarPorEnviosAsync(operadorId, envioIds, cancellationToken))
            .Where(evento => evento.Estado is EstadoEnvio.NoEntregado or EstadoEnvio.EntregadoParcialmente)
            .GroupBy(evento => evento.EnvioId)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Max(evento => evento.Fecha));

        foreach (var (envioId, ultimoIntento) in ultimos)
        {
            var habilitado = DateTime.SpecifyKind(ultimoIntento, DateTimeKind.Utc).AddHours(regla.PlazoEntreIntentosHoras);
            if (habilitado <= ahora)
                continue;

            var envio = await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken);
            var local = new DateTimeOffset(habilitado).ToOffset(RelojOperacion.Offset);
            throw new InvalidOperationException(
                $"El envío {envio?.NumeroSeguimiento} tuvo un intento fallido hace poco: puede volver a salir desde el {local:dd/MM HH\\:mm}.");
        }
    }

    /// <summary>
    /// RF 14, compatibilidad con las franjas comprometidas: una ruta lleva envíos de una sola franja (los que no
    /// tienen franja pueden ir en cualquiera), y una ruta de hoy no puede salir con una franja que ya terminó.
    /// La hora es la del operador (UTC−03:00), igual que en los contratos.
    /// </summary>
    private async Task ValidarFranjasAsync(
        Guid operadorId,
        DateOnly fecha,
        IReadOnlyList<Guid> envioIds,
        CancellationToken cancellationToken)
    {
        var franjaIds = new HashSet<Guid>();
        foreach (var envioId in envioIds)
        {
            var envio = await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken);
            if (envio?.FranjaId is Guid franjaId)
                franjaIds.Add(franjaId);
        }

        var franjas = new List<FranjaHoraria>();
        foreach (var franjaId in franjaIds)
        {
            if (await _cobertura.ObtenerFranjaAsync(operadorId, franjaId, cancellationToken) is { } franja)
                franjas.Add(franja);
        }

        if (franjas.Count > 1)
            throw new InvalidOperationException(
                $"La ruta mezcla envíos de distintas franjas ({string.Join(", ", franjas.Select(f => f.Nombre).Order())}). Armá una ruta por franja.");

        if (franjas.Count == 0)
            return;

        var unica = franjas[0];
        var ahora = DateTimeOffset.UtcNow.ToOffset(RelojOperacion.Offset);
        if (fecha == DateOnly.FromDateTime(ahora.DateTime) && TimeOnly.FromDateTime(ahora.DateTime) >= unica.Hasta)
            throw new InvalidOperationException(
                $"La franja {unica.Nombre} ({unica.Desde:HH\\:mm} a {unica.Hasta:HH\\:mm}) ya terminó hoy: despachá esos envíos para otro día.");
    }
}
