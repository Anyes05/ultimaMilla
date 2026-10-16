using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>
/// Caso de uso que aplica <see cref="Envio.CambiarEstado"/> dentro del operador autenticado
/// y republica el seguimiento en Redis al instante.
/// </summary>
public sealed class CambiarEstadoEnvio : ITransicionEnvio
{
    private readonly IEnvioRepositorio _envios;
    private readonly ICacheLectura _cache;

    public CambiarEstadoEnvio(IEnvioRepositorio envios, ICacheLectura cache)
    {
        _envios = envios;
        _cache = cache;
    }

    public async Task EjecutarAsync(
        Guid envioId,
        Guid operadorId,
        EstadoEnvio nuevoEstado,
        string? usuario = null,
        string? motivo = null,
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        var envio = await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El envío no existe para ese operador.");

        if (!string.IsNullOrWhiteSpace(usuario) || !string.IsNullOrWhiteSpace(motivo))
            envio.CambiarEstado(nuevoEstado, usuario ?? string.Empty, motivo ?? string.Empty);
        else
            envio.CambiarEstado(nuevoEstado);

        await _envios.GuardarAsync(cancellationToken);
        await CacheSeguimiento.PublicarAsync(_cache, envio, cancellationToken);
    }

    public IReadOnlyList<EstadoEnvio> DestinosDesde(EstadoEnvio estadoActual) =>
        TransicionesEnvio.DestinosDesde(estadoActual);
}
