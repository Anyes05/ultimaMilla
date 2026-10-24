using RabbitMQ.Client;

namespace Plataforma.Avisos;

/// <summary>
/// Colas del módulo de avisos. Reintentos con espera creciente sin plugins de RabbitMQ:
/// cada espera es una cola con TTL que, al vencer, devuelve el mensaje a la cola de entregas (dead-letter).
///
///   plataforma.eventos ──envioestadocambiado.v1──▶ plataforma.avisos-comercio      (eventos → crea entregas)
///                                                  plataforma.avisos-comercio.entregas (HTTP al comercio)
///        fallo ─▶ plataforma.avisos-comercio.espera-5s ─TTL─▶ entregas
///        fallo ─▶ plataforma.avisos-comercio.espera-15s ─TTL─▶ entregas ...
///        agotado ─▶ plataforma.avisos-comercio.fallidos (DLQ)
/// </summary>
public static class TopologiaAvisos
{
    public const string RoutingEstadoCambiado = "envioestadocambiado.v1";
    public const string RoutingResultadoAviso = "avisoresultado.v1";

    public const string ColaEventos = "plataforma.avisos-comercio";
    public const string ColaEntregas = "plataforma.avisos-comercio.entregas";
    public const string ColaFallidos = "plataforma.avisos-comercio.fallidos";

    public static string ColaEspera(int segundos) => $"plataforma.avisos-comercio.espera-{segundos}s";

    public static async Task DeclararAsync(
        IChannel canal,
        OpcionesRabbitMq rabbit,
        OpcionesAvisos avisos,
        CancellationToken cancellationToken = default)
    {
        // Mismos parámetros que la API (topic, durable): la declaración es idempotente.
        await canal.ExchangeDeclareAsync(rabbit.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);

        await canal.QueueDeclareAsync(ColaEventos, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await canal.QueueBindAsync(ColaEventos, rabbit.Exchange, RoutingEstadoCambiado,
            cancellationToken: cancellationToken);

        await canal.QueueDeclareAsync(ColaEntregas, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await canal.QueueDeclareAsync(ColaFallidos, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);

        foreach (var segundos in avisos.Esperas)
        {
            await canal.QueueDeclareAsync(
                ColaEspera(segundos),
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = segundos * 1000,
                    ["x-dead-letter-exchange"] = string.Empty,
                    ["x-dead-letter-routing-key"] = ColaEntregas
                },
                cancellationToken: cancellationToken);
        }
    }

    public static ConnectionFactory CrearFabrica(OpcionesRabbitMq rabbit, string nombreCliente) =>
        new()
        {
            HostName = rabbit.Host,
            Port = rabbit.Port,
            UserName = rabbit.Username,
            Password = rabbit.Password,
            ClientProvidedName = nombreCliente
        };
}
