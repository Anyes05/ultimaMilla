using Plataforma.Application;

namespace Plataforma.Infrastructure.Tests;

/// <summary>Publicador de prueba: no habla con RabbitMQ.</summary>
internal sealed class PublicadorMensajesPrueba : IPublicadorMensajes
{
    public bool Disponible { get; init; }
    public List<(string Tipo, string Version, Guid EventId, string Payload)> Publicados { get; } = [];

    public Task PublicarAsync(
        string tipo,
        string version,
        Guid eventId,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        Publicados.Add((tipo, version, eventId, payloadJson));
        return Task.CompletedTask;
    }
}
