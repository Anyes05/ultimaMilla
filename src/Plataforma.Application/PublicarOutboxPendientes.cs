using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Lee filas pendientes de outbox, las publica a la cola y marca <see cref="MensajeOutbox.PublicadoEn"/>.</summary>
public sealed class PublicarOutboxPendientes
{
    private readonly IOutboxRepositorio _outbox;
    private readonly IPublicadorMensajes _publicador;

    public PublicarOutboxPendientes(IOutboxRepositorio outbox, IPublicadorMensajes publicador)
    {
        _outbox = outbox;
        _publicador = publicador;
    }

    public async Task<int> EjecutarAsync(int maximo = 50, CancellationToken cancellationToken = default)
    {
        if (!_publicador.Disponible)
            return 0;

        var pendientes = await _outbox.ListarPendientesAsync(maximo, cancellationToken);
        var publicados = 0;

        foreach (var mensaje in pendientes)
        {
            await _publicador.PublicarAsync(
                mensaje.Tipo,
                mensaje.Version,
                mensaje.EventId,
                mensaje.Payload,
                cancellationToken);

            mensaje.MarcarPublicado(DateTimeOffset.UtcNow);
            publicados++;
        }

        if (publicados > 0)
            await _outbox.GuardarAsync(cancellationToken);

        return publicados;
    }

    public async Task<bool> EjecutarUnoAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        if (!_publicador.Disponible)
            return false;

        var mensaje = await _outbox.ObtenerAsync(eventId, cancellationToken);
        if (mensaje is null || mensaje.PublicadoEn is not null)
            return false;

        await _publicador.PublicarAsync(
            mensaje.Tipo,
            mensaje.Version,
            mensaje.EventId,
            mensaje.Payload,
            cancellationToken);

        mensaje.MarcarPublicado(DateTimeOffset.UtcNow);
        await _outbox.GuardarAsync(cancellationToken);
        return true;
    }
}
