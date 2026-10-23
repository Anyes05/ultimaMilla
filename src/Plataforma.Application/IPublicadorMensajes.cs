namespace Plataforma.Application;

/// <summary>Publica el payload del contrato de mensaje a la cola (RabbitMQ).</summary>
public interface IPublicadorMensajes
{
    bool Disponible { get; }

    Task PublicarAsync(
        string tipo,
        string version,
        Guid eventId,
        string payloadJson,
        CancellationToken cancellationToken = default);
}
