using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class TransicionEnvio : ITransicionEnvio
{
    private readonly IEnvioRepositorio _envios;

    public TransicionEnvio(IEnvioRepositorio envios)
    {
        _envios = envios;
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
    }

    public IReadOnlyList<EstadoEnvio> DestinosDesde(EstadoEnvio estadoActual) =>
        TransicionesEnvio.DestinosDesde(estadoActual);
}
