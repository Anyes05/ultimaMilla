using System.Text.Json;
using Plataforma.Domain;

namespace Plataforma.Application;

public sealed record EventoSeguimiento(EstadoEnvio Estado, DateTime Fecha);

/// <summary>Lo que ve el destinatario (letra 4, RF 25, 26 y 35): sin datos personales ni identificadores internos.</summary>
public sealed record DetalleSeguimiento(
    string NumeroSeguimiento,
    EstadoEnvio Estado,
    int IntentosRealizados,
    string Operador,
    string ColorMarca,
    string ZonaHoraria,
    int BultosEntregados,
    int BultosTotal,
    DateOnly? FechaPrevista,
    DateTime? EstimadaAntesDeUtc,
    IReadOnlyList<EventoSeguimiento> Historial,
    bool PuedeReprogramar);

/// <summary>
/// Detalle del seguimiento público. Se cachea porque es la lectura más frecuente del sistema: cada cambio de estado
/// lo descarta (<see cref="CacheSeguimiento"/>), y la vigencia es corta porque el plazo y la estimación dependen de la hora.
/// </summary>
public sealed class ObtenerDetalleSeguimiento
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(2);

    private readonly IEnvioRepositorio _envios;
    private readonly IBultoRepositorio _bultos;
    private readonly IHistorialEstadoRepositorio _historial;
    private readonly IOperadorRepositorio _operadores;
    private readonly IRutaRepositorio _rutas;
    private readonly IReglaOperativaRepositorio _reglas;
    private readonly ICacheLectura _cache;

    public ObtenerDetalleSeguimiento(
        IEnvioRepositorio envios,
        IBultoRepositorio bultos,
        IHistorialEstadoRepositorio historial,
        IOperadorRepositorio operadores,
        IRutaRepositorio rutas,
        IReglaOperativaRepositorio reglas,
        ICacheLectura cache)
    {
        _envios = envios;
        _bultos = bultos;
        _historial = historial;
        _operadores = operadores;
        _rutas = rutas;
        _reglas = reglas;
        _cache = cache;
    }

    public Task<DetalleSeguimiento?> PorTokenAsync(string token, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(token)
            ? Task.FromResult<DetalleSeguimiento?>(null)
            : ConsultarAsync(
                ClavesCache.DetalleSeguimiento(token.Trim()),
                () => _envios.ObtenerPorTokenAsync(token.Trim(), cancellationToken),
                cancellationToken);

    public Task<DetalleSeguimiento?> PorNumeroAsync(string numero, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(numero)
            ? Task.FromResult<DetalleSeguimiento?>(null)
            : ConsultarAsync(
                ClavesCache.DetalleSeguimientoPorNumero(numero.Trim()),
                () => _envios.ObtenerPorNumeroAsync(numero.Trim(), cancellationToken),
                cancellationToken);

    /// <summary>Hora del último intento fallido: desde ahí corre el plazo de reprogramación.</summary>
    internal static DateTime? UltimoIntento(IEnumerable<HistorialEstado> historial) =>
        historial.Where(evento => evento.Estado == EstadoEnvio.NoEntregado)
            .Select(evento => (DateTime?)evento.Fecha)
            .Max();

    private async Task<DetalleSeguimiento?> ConsultarAsync(
        string clave,
        Func<Task<Envio?>> buscar,
        CancellationToken cancellationToken)
    {
        var cacheado = await _cache.LeerAsync(clave, cancellationToken);
        if (cacheado is not null)
            return JsonSerializer.Deserialize<DetalleSeguimiento>(cacheado, ObtenerRutasDeHoy.OpcionesJson);

        var envio = await buscar();
        if (envio is null)
            return null;

        var detalle = await ArmarAsync(envio, cancellationToken);
        if (detalle is not null)
            await _cache.GuardarAsync(clave, JsonSerializer.Serialize(detalle, ObtenerRutasDeHoy.OpcionesJson), Vigencia, cancellationToken);
        return detalle;
    }

    private async Task<DetalleSeguimiento?> ArmarAsync(Envio envio, CancellationToken cancellationToken)
    {
        var operador = await _operadores.ObtenerAsync(envio.OperadorId, cancellationToken);
        if (operador is null)
            return null;

        var ahora = DateTime.UtcNow;
        var historial = await _historial.ListarPorEnvioAsync(envio.OperadorId, envio.Id, cancellationToken);
        var bultos = await _bultos.ListarPorEnvioAsync(envio.OperadorId, envio.Id, cancellationToken);
        var (fechaPrevista, estimadaAntesDe) = await EstimarAsync(envio, historial, ahora, cancellationToken);
        var regla = await _reglas.ObtenerVigenteAsync(envio.OperadorId, ahora, cancellationToken);

        return new DetalleSeguimiento(
            envio.NumeroSeguimiento,
            envio.Estado,
            envio.IntentosRealizados,
            operador.Nombre,
            operador.ColorMarca,
            operador.ZonaHoraria,
            // Una entrega informada a nivel de envío deja los bultos sin actualizar: si está entregado, lo están todos.
            envio.Estado == EstadoEnvio.Entregado ? bultos.Count : bultos.Count(bulto => bulto.Estado == EstadoBulto.Entregado),
            bultos.Count,
            fechaPrevista,
            estimadaAntesDe,
            historial.Select(evento => new EventoSeguimiento(evento.Estado, evento.Fecha)).ToList(),
            regla?.PermiteReprogramar(envio.Estado, envio.IntentosRealizados, UltimoIntento(historial), ahora) == true);
    }

    /// <summary>
    /// Sin franjas horarias todavía (RF 2), la ventana es el día de la ruta o, sin ruta, el plazo comprometido por el
    /// operador desde el alta o la última reprogramación, con la regla vigente al crear el envío (letra 6.6).
    /// </summary>
    private async Task<(DateOnly? Fecha, DateTime? AntesDe)> EstimarAsync(
        Envio envio,
        IReadOnlyList<HistorialEstado> historial,
        DateTime ahora,
        CancellationToken cancellationToken)
    {
        if (envio.Estado is not (EstadoEnvio.Admitido or EstadoEnvio.EnDeposito or EstadoEnvio.AsignadoARuta
            or EstadoEnvio.EnTransito or EstadoEnvio.EntregadoParcialmente or EstadoEnvio.Reprogramado))
            return (null, null);

        if (envio.RutaId is Guid rutaId &&
            await _rutas.ObtenerAsync(envio.OperadorId, rutaId, cancellationToken) is { } ruta)
            return (ruta.Fecha, null);

        var regla = await _reglas.ObtenerVigenteAsync(envio.OperadorId, envio.FechaCreacion, cancellationToken);
        if (regla is null)
            return (null, null);

        var desde = historial.Where(evento => evento.Estado == EstadoEnvio.Reprogramado)
            .Select(evento => (DateTime?)evento.Fecha)
            .Max() ?? envio.FechaCreacion;
        var antesDe = desde.AddHours(regla.PlazoEntregaHoras);
        return antesDe > ahora ? (null, antesDe) : (null, null);
    }
}
