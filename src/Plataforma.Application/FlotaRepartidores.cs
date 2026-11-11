using Plataforma.Domain;

namespace Plataforma.Application;

public interface IPosicionRepositorio
{
    Task AgregarAsync(IEnumerable<PosicionRepartidor> posiciones, CancellationToken cancellationToken = default);

    /// <summary>La última posición de cada repartidor del operador desde el instante indicado.</summary>
    Task<IReadOnlyList<PosicionRepartidor>> UltimasPorRepartidorAsync(
        Guid operadorId,
        DateTime desdeUtc,
        CancellationToken cancellationToken = default);

    Task GuardarAsync(CancellationToken cancellationToken = default);
}

/// <summary>Una posición tal como la manda la app (contrato de /api/repartidor/posiciones).</summary>
public sealed record PosicionReportada(
    decimal Latitud,
    decimal Longitud,
    DateTimeOffset FechaDispositivo,
    decimal? PrecisionMetros = null);

public sealed record ResultadoPosiciones(int Aceptadas, int Descartadas);

/// <summary>
/// RF 23: guarda las posiciones que manda la app durante la jornada. Llegan en lotes porque la app las junta
/// mientras está sin señal. Se descartan las que no sirven para seguir la jornada (más de 24 h de antigüedad o
/// con hora en el futuro) en lugar de rechazar el lote entero.
/// </summary>
public sealed class RegistrarPosiciones
{
    public const int MaximoPorLote = 500;
    private static readonly TimeSpan Antiguedad = TimeSpan.FromHours(24);
    private static readonly TimeSpan ToleranciaReloj = TimeSpan.FromMinutes(5);

    private readonly IPosicionRepositorio _posiciones;

    public RegistrarPosiciones(IPosicionRepositorio posiciones) => _posiciones = posiciones;

    public async Task<ResultadoPosiciones> EjecutarAsync(
        Guid operadorId,
        Guid repartidorId,
        IReadOnlyList<PosicionReportada> lote,
        CancellationToken cancellationToken = default)
    {
        if (lote is null || lote.Count == 0)
            throw new ArgumentException("El lote necesita al menos una posición.");
        if (lote.Count > MaximoPorLote)
            throw new ArgumentException($"Un lote admite como máximo {MaximoPorLote} posiciones.");
        if (lote.Any(p => p.Latitud is < -90 or > 90 || p.Longitud is < -180 or > 180 || p.PrecisionMetros is < 0))
            throw new ArgumentException("Hay posiciones con coordenadas o precisión fuera de rango.");

        var ahora = DateTime.UtcNow;
        var validas = lote
            .Where(p => p.FechaDispositivo.UtcDateTime <= ahora + ToleranciaReloj && p.FechaDispositivo.UtcDateTime >= ahora - Antiguedad)
            .Select(p => PosicionRepartidor.Registrar(
                Guid.NewGuid(), operadorId, repartidorId, p.Latitud, p.Longitud, p.FechaDispositivo, ahora, p.PrecisionMetros))
            .ToList();

        if (validas.Count > 0)
        {
            await _posiciones.AgregarAsync(validas, cancellationToken);
            await _posiciones.GuardarAsync(cancellationToken);
        }

        return new ResultadoPosiciones(validas.Count, lote.Count - validas.Count);
    }
}

/// <summary>Un repartidor con ruta hoy y su última posición conocida (RF 29).</summary>
public sealed record VehiculoEnFlota(
    Guid RepartidorId,
    string Repartidor,
    int Envios,
    int Entregados,
    decimal? Latitud,
    decimal? Longitud,
    decimal? PrecisionMetros,
    DateTime? UltimaPosicionUtc)
{
    /// <summary>Sin posición o sin reportar hace más de diez minutos.</summary>
    public bool SinSenal(DateTime ahoraUtc) =>
        UltimaPosicionUtc is not DateTime ultima || ahoraUtc - ultima > ObtenerFlota.SinSenalDespuesDe;
}

/// <summary>Flota del día para el Tablero: repartidores con ruta, su avance y su última posición.</summary>
public sealed class ObtenerFlota
{
    public static readonly TimeSpan SinSenalDespuesDe = TimeSpan.FromMinutes(10);

    private readonly ListarRutasDelDia _rutas;
    private readonly IPosicionRepositorio _posiciones;

    public ObtenerFlota(ListarRutasDelDia rutas, IPosicionRepositorio posiciones)
    {
        _rutas = rutas;
        _posiciones = posiciones;
    }

    public async Task<IReadOnlyList<VehiculoEnFlota>> EjecutarAsync(
        Guid operadorId,
        DateOnly fecha,
        DateTime inicioDelDiaUtc,
        CancellationToken cancellationToken = default)
    {
        var rutas = await _rutas.EjecutarAsync(operadorId, fecha, cancellationToken);
        if (rutas.Count == 0)
            return [];

        var ultimas = (await _posiciones.UltimasPorRepartidorAsync(operadorId, inicioDelDiaUtc, cancellationToken))
            .ToDictionary(posicion => posicion.RepartidorId);

        return rutas
            .GroupBy(ruta => (ruta.RepartidorId, ruta.Repartidor))
            .Select(grupo =>
            {
                var envios = grupo.SelectMany(ruta => ruta.Envios).ToList();
                ultimas.TryGetValue(grupo.Key.RepartidorId, out var posicion);
                return new VehiculoEnFlota(
                    grupo.Key.RepartidorId,
                    grupo.Key.Repartidor,
                    envios.Count,
                    envios.Count(envio => envio.Estado == EstadoEnvio.Entregado),
                    posicion?.Latitud,
                    posicion?.Longitud,
                    posicion?.PrecisionMetros,
                    posicion is null ? null : DateTime.SpecifyKind(posicion.FechaUtc, DateTimeKind.Utc));
            })
            .OrderBy(vehiculo => vehiculo.Repartidor)
            .ToList();
    }
}
