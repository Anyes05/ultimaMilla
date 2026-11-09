using Plataforma.Domain;

namespace Plataforma.Application;

public enum ResultadoReprogramacion
{
    Realizada,
    NoEncontrado,
    NoPermitida
}

/// <summary>
/// El destinatario pide otra visita (RF 26), sujeto a las reglas del operador. Solo con el token del enlace que
/// recibió: el número de seguimiento alcanza para consultar, no para cambiar un envío.
/// </summary>
public sealed class SolicitarReprogramacion
{
    private readonly IEnvioRepositorio _envios;
    private readonly IHistorialEstadoRepositorio _historial;
    private readonly IReglaOperativaRepositorio _reglas;
    private readonly ITransicionEnvio _transicion;

    public SolicitarReprogramacion(
        IEnvioRepositorio envios,
        IHistorialEstadoRepositorio historial,
        IReglaOperativaRepositorio reglas,
        ITransicionEnvio transicion)
    {
        _envios = envios;
        _historial = historial;
        _reglas = reglas;
        _transicion = transicion;
    }

    public async Task<ResultadoReprogramacion> EjecutarAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return ResultadoReprogramacion.NoEncontrado;

        var envio = await _envios.ObtenerPorTokenAsync(token.Trim(), cancellationToken);
        if (envio is null)
            return ResultadoReprogramacion.NoEncontrado;

        var ahora = DateTime.UtcNow;
        var regla = await _reglas.ObtenerVigenteAsync(envio.OperadorId, ahora, cancellationToken);
        var historial = await _historial.ListarPorEnvioAsync(envio.OperadorId, envio.Id, cancellationToken);
        if (regla?.PermiteReprogramar(envio.Estado, envio.IntentosRealizados, ObtenerDetalleSeguimiento.UltimoIntento(historial), ahora) != true)
            return ResultadoReprogramacion.NoPermitida;

        await _transicion.EjecutarAsync(
            envio.Id,
            envio.OperadorId,
            EstadoEnvio.Reprogramado,
            "destinatario",
            "Solicitada por el destinatario",
            cancellationToken);

        return ResultadoReprogramacion.Realizada;
    }
}
