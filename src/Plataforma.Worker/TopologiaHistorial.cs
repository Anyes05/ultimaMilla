using Plataforma.Avisos;
using RabbitMQ.Client;

namespace Plataforma.Worker;

public static class TopologiaHistorial
{
    public const string ColaEventos = "plataforma.historial-eventos";
    public const string ColaFallidos = "plataforma.historial-eventos.fallidos";
    public static string ColaEspera(int segundos) => $"plataforma.historial-eventos.espera-{segundos}s";

    public static async Task DeclararAsync(IChannel canal, OpcionesRabbitMq rabbit, OpcionesAvisos avisos, CancellationToken cancellationToken = default)
    {
        await canal.ExchangeDeclareAsync(rabbit.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await canal.QueueDeclareAsync(ColaEventos, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await canal.QueueBindAsync(ColaEventos, rabbit.Exchange, TopologiaAvisos.RoutingEstadoCambiado, cancellationToken: cancellationToken);
        await canal.QueueDeclareAsync(ColaFallidos, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        foreach (var segundos in avisos.Esperas)
            await canal.QueueDeclareAsync(ColaEspera(segundos), durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-message-ttl"] = segundos * 1000, ["x-dead-letter-exchange"] = string.Empty, ["x-dead-letter-routing-key"] = ColaEventos }, cancellationToken: cancellationToken);
    }
}