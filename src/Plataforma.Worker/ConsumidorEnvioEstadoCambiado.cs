using System.Text;
using Microsoft.Extensions.Options;
using Plataforma.Application.Mensajes;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Plataforma.Worker;

/// <summary>
/// Consume EnvioEstadoCambiado v1 desde RabbitMQ.
/// Paso 1: declara su cola, la enlaza al exchange de la API y registra cada evento.
/// Los pasos siguientes agregan el aviso HTTP firmado, reintentos, DLQ e idempotencia.
/// </summary>
public sealed class ConsumidorEnvioEstadoCambiado : BackgroundService
{
    private static readonly TimeSpan EsperaReconexion = TimeSpan.FromSeconds(5);

    private readonly OpcionesRabbitMqWorker _rabbit;
    private readonly OpcionesAvisos _avisos;
    private readonly ILogger<ConsumidorEnvioEstadoCambiado> _logger;

    public ConsumidorEnvioEstadoCambiado(
        IOptions<OpcionesRabbitMqWorker> rabbit,
        IOptions<OpcionesAvisos> avisos,
        ILogger<ConsumidorEnvioEstadoCambiado> logger)
    {
        _rabbit = rabbit.Value;
        _avisos = avisos.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_rabbit.Host))
        {
            _logger.LogError("Falta RabbitMq:Host; el worker no puede consumir mensajes.");
            return;
        }

        // RabbitMQ puede arrancar después que el worker (pod o compose): se reintenta hasta conectar.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumirAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sin conexión con RabbitMQ en {Host}:{Port}; reintento en {Segundos}s.",
                    _rabbit.Host, _rabbit.Port, EsperaReconexion.TotalSeconds);
                await Task.Delay(EsperaReconexion, stoppingToken);
            }
        }
    }

    private async Task ConsumirAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _rabbit.Host,
            Port = _rabbit.Port,
            UserName = _rabbit.Username,
            Password = _rabbit.Password,
            ClientProvidedName = "plataforma-worker"
        };

        await using var conexion = await factory.CreateConnectionAsync(stoppingToken);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: stoppingToken);

        // Mismos parámetros que la API (topic, durable) para que la declaración sea idempotente.
        await canal.ExchangeDeclareAsync(_rabbit.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: stoppingToken);
        await canal.QueueDeclareAsync(_avisos.Cola, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: stoppingToken);
        await canal.QueueBindAsync(_avisos.Cola, _rabbit.Exchange, _avisos.RoutingKey,
            cancellationToken: stoppingToken);
        await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: _avisos.Prefetch, global: false,
            cancellationToken: stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += async (_, entrega) =>
        {
            var json = Encoding.UTF8.GetString(entrega.Body.Span);
            EnvioEstadoCambiado? evento = null;
            try
            {
                evento = EnvioEstadoCambiado.Deserializar(json);
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "Mensaje con JSON inválido (MessageId={MessageId}).", entrega.BasicProperties.MessageId);
            }

            if (evento is null || evento.EventId == Guid.Empty)
            {
                // Un mensaje que no respeta el contrato no se reencola: volvería a fallar para siempre.
                await canal.BasicRejectAsync(entrega.DeliveryTag, requeue: false, stoppingToken);
                return;
            }

            _logger.LogInformation(
                "Recibido EnvioEstadoCambiado EventId={EventId} Operador={OperadorId} Envio={EnvioId} {Anterior} -> {Nuevo}",
                evento.EventId, evento.OperadorId, evento.EnvioId, evento.EstadoAnterior, evento.EstadoNuevo);

            // Ack manual: solo se confirma cuando el procesamiento terminó.
            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, stoppingToken);
        };

        await canal.BasicConsumeAsync(_avisos.Cola, autoAck: false, consumidor, stoppingToken);
        _logger.LogInformation("Worker escuchando la cola {Cola} ({Exchange} / {RoutingKey}).",
            _avisos.Cola, _rabbit.Exchange, _avisos.RoutingKey);

        // Mantiene la conexión viva hasta que se apague el host o se caiga la conexión.
        var cerrada = new TaskCompletionSource();
        conexion.ConnectionShutdownAsync += (_, _) =>
        {
            cerrada.TrySetResult();
            return Task.CompletedTask;
        };
        await cerrada.Task.WaitAsync(stoppingToken);
        throw new InvalidOperationException("Se cerró la conexión con RabbitMQ.");
    }
}
