namespace Plataforma.Worker;

/// <summary>
/// Conexión a RabbitMQ. Usa la misma sección "RabbitMq" que Backoffice y Portal
/// (RabbitMq__Host, RabbitMq__Username, RabbitMq__Password) para no inventar variables nuevas.
/// </summary>
public sealed class OpcionesRabbitMqWorker
{
    public const string Seccion = "RabbitMq";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>Exchange topic que declara la API al publicar el outbox (contracts/mensajes/v1).</summary>
    public string Exchange { get; set; } = "plataforma.eventos";
}

/// <summary>Topología propia del worker: su cola y qué eventos escucha.</summary>
public sealed class OpcionesAvisos
{
    public const string Seccion = "Avisos";

    public string Cola { get; set; } = "plataforma.avisos-comercio";

    /// <summary>Routing key del contrato v1 de EnvioEstadoCambiado.</summary>
    public string RoutingKey { get; set; } = "envioestadocambiado.v1";

    /// <summary>Mensajes sin confirmar que RabbitMQ entrega a la vez a este consumidor.</summary>
    public ushort Prefetch { get; set; } = 10;
}
