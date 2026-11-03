using Plataforma.Avisos;
using RabbitMQ.Client;

namespace Plataforma.Worker.Push;

/// <summary>
/// Cola propia del push de reasignación, separada de avisos e historial: si FCM falla, lo demás sigue.
/// Reintentos con espera creciente (colas con TTL que vuelven a la cola principal) y cola de fallidos.
/// </summary>
public static class TopologiaPush
{
    /// <summary>Evento que publica el outbox al reasignar un envío (contrato de Ana, ver propuesta).</summary>
    public const string RoutingEnvioReasignado = "envioreasignado.v1";

    public const string ColaEventos = "plataforma.push-repartidor";
    public const string ColaFallidos = "plataforma.push-repartidor.fallidos";
    public static string ColaEspera(int segundos) => $"plataforma.push-repartidor.espera-{segundos}s";

    public static async Task DeclararAsync(IChannel canal, OpcionesRabbitMq rabbit, OpcionesAvisos avisos, CancellationToken cancellationToken = default)
    {
        await canal.ExchangeDeclareAsync(rabbit.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await canal.QueueDeclareAsync(ColaEventos, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await canal.QueueBindAsync(ColaEventos, rabbit.Exchange, RoutingEnvioReasignado, cancellationToken: cancellationToken);
        await canal.QueueDeclareAsync(ColaFallidos, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        foreach (var segundos in avisos.Esperas)
            await canal.QueueDeclareAsync(ColaEspera(segundos), durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = segundos * 1000,
                    ["x-dead-letter-exchange"] = string.Empty,
                    ["x-dead-letter-routing-key"] = ColaEventos
                },
                cancellationToken: cancellationToken);
    }
}

/// <summary>Contrato propuesto de EnvioReasignado v1 (docs/propuestas/push-reasignacion.md); lo cierra Ana.</summary>
public sealed record EventoEnvioReasignado(
    Guid EventId,
    DateTimeOffset Fecha,
    Guid OperadorId,
    Guid EnvioId,
    string? NumeroSeguimiento,
    Guid? RepartidorAnteriorId,
    Guid RepartidorNuevoId,
    Guid? RutaNuevaId,
    bool Urgente);
