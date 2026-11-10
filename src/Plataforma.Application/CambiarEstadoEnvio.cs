using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>
/// Aplica el cambio de estado, escribe outbox en la misma transacción,
/// intenta publicar el evento y republica el seguimiento en Redis.
/// </summary>
public sealed class CambiarEstadoEnvio : ITransicionEnvio
{
    private readonly IEnvioRepositorio _envios;
    private readonly IRutaRepositorio _rutas;
    private readonly RegistrarOutboxEstado _outbox;
    private readonly PublicarOutboxPendientes _publicar;
    private readonly ICacheLectura _cache;

    public CambiarEstadoEnvio(
        IEnvioRepositorio envios,
        IRutaRepositorio rutas,
        RegistrarOutboxEstado outbox,
        PublicarOutboxPendientes publicar,
        ICacheLectura cache)
    {
        _envios = envios;
        _rutas = rutas;
        _outbox = outbox;
        _publicar = publicar;
        _cache = cache;
    }

    public async Task<ResultadoTransicionEnvio> EjecutarAsync(
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

        var estadoAnterior = envio.Estado;

        if (!string.IsNullOrWhiteSpace(usuario) || !string.IsNullOrWhiteSpace(motivo))
            envio.CambiarEstado(nuevoEstado, usuario ?? string.Empty, motivo ?? string.Empty);
        else
            envio.CambiarEstado(nuevoEstado);

        var eventId = await _outbox.EjecutarAsync(
            operadorId,
            envio.Id,
            estadoAnterior,
            envio.Estado,
            cancellationToken,
            motivo: motivo ?? "",
            responsable: usuario ?? "");

        await _envios.GuardarAsync(cancellationToken);

        if (eventId is Guid id)
            await _publicar.EjecutarUnoAsync(id, cancellationToken);

        await CacheSeguimiento.PublicarAsync(_cache, envio, cancellationToken);

        if (envio.RutaId is Guid rutaId)
        {
            var ruta = await _rutas.ObtenerAsync(operadorId, rutaId, cancellationToken);
            if (ruta is not null)
                await _cache.QuitarAsync(ObtenerRutasDeHoy.Clave(operadorId, ruta.RepartidorId, ruta.Fecha), cancellationToken);
        }

        return new ResultadoTransicionEnvio(envio.Id, estadoAnterior, envio.Estado, eventId);
    }

    public IReadOnlyList<EstadoEnvio> DestinosDesde(EstadoEnvio estadoActual) =>
        TransicionesEnvio.DestinosDesde(estadoActual);
}
