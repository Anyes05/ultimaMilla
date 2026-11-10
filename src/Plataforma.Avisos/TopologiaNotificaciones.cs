using RabbitMQ.Client;

namespace Plataforma.Avisos;

/// <summary>
/// Cola propia de las notificaciones al destinatario, separada de avisos e historial: si el correo falla, lo demás sigue.
/// Reintentos con espera creciente (colas con TTL que vuelven a la cola principal) y cola de fallidos.
/// </summary>
public static class TopologiaNotificaciones
{
    public const string RoutingEnvioAdmitido = "envioadmitido.v1";

    public const string ColaEventos = "plataforma.notificaciones-destinatario";
    public const string ColaFallidos = "plataforma.notificaciones-destinatario.fallidos";

    public static string ColaEspera(int segundos) => $"plataforma.notificaciones-destinatario.espera-{segundos}s";

    public static async Task DeclararAsync(IChannel canal, OpcionesRabbitMq rabbit, OpcionesAvisos avisos, CancellationToken cancellationToken = default)
    {
        await canal.ExchangeDeclareAsync(rabbit.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await canal.QueueDeclareAsync(ColaEventos, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await canal.QueueBindAsync(ColaEventos, rabbit.Exchange, TopologiaAvisos.RoutingEstadoCambiado, cancellationToken: cancellationToken);
        await canal.QueueBindAsync(ColaEventos, rabbit.Exchange, RoutingEnvioAdmitido, cancellationToken: cancellationToken);
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
