using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Application;
using RabbitMQ.Client;

namespace Plataforma.Infrastructure;

public sealed class OpcionesRabbitMq
{
    public const string Seccion = "RabbitMq";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string Exchange { get; set; } = "plataforma.eventos";
}

public sealed class PublicadorMensajesRabbitMq : IPublicadorMensajes, IAsyncDisposable
{
    private readonly OpcionesRabbitMq _opciones;
    private readonly ILogger<PublicadorMensajesRabbitMq> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _conexion;
    private IChannel? _canal;

    public PublicadorMensajesRabbitMq(
        IOptions<OpcionesRabbitMq> opciones,
        ILogger<PublicadorMensajesRabbitMq> logger)
    {
        _opciones = opciones.Value;
        _logger = logger;
    }

    public bool Disponible => !string.IsNullOrWhiteSpace(_opciones.Host);

    public async Task PublicarAsync(
        string tipo,
        string version,
        Guid eventId,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        if (!Disponible)
            throw new InvalidOperationException("RabbitMQ no está configurado.");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var canal = await ObtenerCanalAsync(cancellationToken);
            var routingKey = $"{tipo.ToLowerInvariant()}.v{version}";
            var cuerpo = Encoding.UTF8.GetBytes(payloadJson);
            var propiedades = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = eventId.ToString(),
                Type = tipo,
                Headers = new Dictionary<string, object?>
                {
                    ["tipo"] = tipo,
                    ["version"] = version,
                    ["eventId"] = eventId.ToString()
                }
            };

            await canal.BasicPublishAsync(
                exchange: _opciones.Exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: propiedades,
                body: cuerpo,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Publicado {Tipo} v{Version} EventId={EventId} routingKey={RoutingKey}",
                tipo, version, eventId, routingKey);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> ObtenerCanalAsync(CancellationToken cancellationToken)
    {
        if (_canal is { IsOpen: true })
            return _canal;

        var factory = new ConnectionFactory
        {
            HostName = _opciones.Host,
            Port = _opciones.Port,
            UserName = _opciones.Username,
            Password = _opciones.Password
        };

        _conexion = await factory.CreateConnectionAsync(cancellationToken);
        _canal = await _conexion.CreateChannelAsync(cancellationToken: cancellationToken);
        await _canal.ExchangeDeclareAsync(
            exchange: _opciones.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        return _canal;
    }

    public async ValueTask DisposeAsync()
    {
        if (_canal is not null)
            await _canal.DisposeAsync();
        if (_conexion is not null)
            await _conexion.DisposeAsync();
        _lock.Dispose();
    }
}
