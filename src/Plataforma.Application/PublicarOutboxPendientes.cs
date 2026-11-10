using Microsoft.Extensions.Logging;
using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Lee filas pendientes de outbox, las publica a la cola y marca <see cref="MensajeOutbox.PublicadoEn"/>.</summary>
public sealed class PublicarOutboxPendientes
{
    private readonly IOutboxRepositorio _outbox;
    private readonly IPublicadorMensajes _publicador;
    private readonly ILogger<PublicarOutboxPendientes>? _logger;

    public PublicarOutboxPendientes(
        IOutboxRepositorio outbox,
        IPublicadorMensajes publicador,
        ILogger<PublicarOutboxPendientes>? logger = null)
    {
        _outbox = outbox;
        _publicador = publicador;
        _logger = logger;
    }

    public async Task<int> EjecutarAsync(int maximo = 50, CancellationToken cancellationToken = default)
    {
        if (!_publicador.Disponible)
            return 0;

        var pendientes = await _outbox.ListarPendientesAsync(maximo, cancellationToken);
        var publicados = 0;

        foreach (var mensaje in pendientes)
        {
            // Si otra réplica ya lo marcó, no republicar (idempotencia de drenado).
            if (mensaje.PublicadoEn is not null)
                continue;

            try
            {
                await _publicador.PublicarAsync(
                    mensaje.Tipo,
                    mensaje.Version,
                    mensaje.EventId,
                    mensaje.Payload,
                    mensaje.TraceParent,
                    cancellationToken);

                mensaje.MarcarPublicado(RelojOperacion.Ahora());
                publicados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // La fila queda pendiente: el publicador en segundo plano reintenta.
                _logger?.LogWarning(ex, "No se pudo publicar outbox {EventId}; queda pendiente.", mensaje.EventId);
            }
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

        try
        {
            await _publicador.PublicarAsync(
                mensaje.Tipo,
                mensaje.Version,
                mensaje.EventId,
                mensaje.Payload,
                mensaje.TraceParent,
                cancellationToken);

            mensaje.MarcarPublicado(RelojOperacion.Ahora());
            await _outbox.GuardarAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No propagar: el cambio de dominio ya se guardó; el background drenará el outbox.
            _logger?.LogWarning(ex, "Publicación inmediata falló para {EventId}; se reintentará en background.", eventId);
            return false;
        }
    }
}
