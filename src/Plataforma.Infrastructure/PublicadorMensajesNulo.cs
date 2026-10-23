using Microsoft.Extensions.Logging;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

/// <summary>Sin RabbitMQ configurado: no publica (los mensajes quedan pendientes en outbox).</summary>
public sealed class PublicadorMensajesNulo : IPublicadorMensajes
{
    private readonly ILogger<PublicadorMensajesNulo> _logger;

    public PublicadorMensajesNulo(ILogger<PublicadorMensajesNulo> logger)
    {
        _logger = logger;
    }

    public bool Disponible => false;

    public Task PublicarAsync(
        string tipo,
        string version,
        Guid eventId,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Publicador nulo: no se publica {Tipo} v{Version} EventId={EventId}",
            tipo, version, eventId);
        return Task.CompletedTask;
    }
}
