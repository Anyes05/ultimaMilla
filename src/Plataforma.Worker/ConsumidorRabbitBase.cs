using Microsoft.Extensions.Options;
using Plataforma.Avisos;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Plataforma.Worker;

/// <summary>
/// Conexión, topología, reconexión y ack manual compartidos por los consumidores del worker.
/// Cada subclase decide qué hacer con un mensaje; si lanza una excepción no prevista,
/// el mensaje se devuelve a la cola (nack con requeue) tras una pausa para no girar en falso.
/// </summary>
public abstract class ConsumidorRabbitBase : BackgroundService
{
    private static readonly TimeSpan EsperaReconexion = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PausaTrasError = TimeSpan.FromSeconds(2);

    protected ConsumidorRabbitBase(
        IOptions<OpcionesRabbitMq> rabbit,
        IOptions<OpcionesAvisos> avisos,
        ILogger logger)
    {
        Rabbit = rabbit.Value;
        Avisos = avisos.Value;
        Logger = logger;
    }

    protected OpcionesRabbitMq Rabbit { get; }
    protected OpcionesAvisos Avisos { get; }
    protected ILogger Logger { get; }

    protected abstract string Cola { get; }

    /// <summary>Procesa el mensaje. Debe confirmar (ack/reject) él mismo los casos que resuelve.</summary>
    protected abstract Task ProcesarAsync(IChannel canal, BasicDeliverEventArgs entrega, CancellationToken stoppingToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Rabbit.Configurado)
        {
            Logger.LogError("Falta RabbitMq:Host; {Consumidor} no puede iniciar.", GetType().Name);
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
                Logger.LogWarning(ex, "{Consumidor}: sin conexión con RabbitMQ en {Host}:{Port}; reintento en {Segundos}s.",
                    GetType().Name, Rabbit.Host, Rabbit.Port, EsperaReconexion.TotalSeconds);
                await Task.Delay(EsperaReconexion, stoppingToken);
            }
        }
    }

    private async Task ConsumirAsync(CancellationToken stoppingToken)
    {
        await using var conexion = await TopologiaAvisos.CrearFabrica(Rabbit, $"plataforma-worker-{GetType().Name}")
            .CreateConnectionAsync(stoppingToken);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: stoppingToken);

        await TopologiaAvisos.DeclararAsync(canal, Rabbit, Avisos, stoppingToken);
        await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: Avisos.Prefetch, global: false,
            cancellationToken: stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += async (_, entrega) =>
        {
            try
            {
                await ProcesarAsync(canal, entrega, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Apagado: el mensaje sin ack vuelve solo a la cola al cerrarse el canal.
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{Consumidor}: error procesando MessageId={MessageId}; se reencola.",
                    GetType().Name, entrega.BasicProperties.MessageId);
                await Task.Delay(PausaTrasError, CancellationToken.None);
                await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
            }
        };

        await canal.BasicConsumeAsync(Cola, autoAck: false, consumidor, stoppingToken);
        Logger.LogInformation("{Consumidor} escuchando la cola {Cola}.", GetType().Name, Cola);

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
