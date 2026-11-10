using Plataforma.Application;

namespace Plataforma.Infrastructure.Tests;

/// <summary>Publicador de prueba: no habla con RabbitMQ.</summary>
internal sealed class PublicadorMensajesPrueba : IPublicadorMensajes
{
    public bool Disponible { get; init; }
    public bool Falla { get; init; }
    public List<(string Tipo, string Version, Guid EventId, string Payload, string? TraceParent)> Publicados { get; } = [];

    public Task PublicarAsync(
        string tipo,
        string version,
        Guid eventId,
        string payloadJson,
        string? traceParent = null,
        CancellationToken cancellationToken = default)
    {
        if (Falla)
            throw new InvalidOperationException("RabbitMQ caído.");

        Publicados.Add((tipo, version, eventId, payloadJson, traceParent));
        return Task.CompletedTask;
    }
}
