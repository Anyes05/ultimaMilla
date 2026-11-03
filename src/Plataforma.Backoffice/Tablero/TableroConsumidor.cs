using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Plataforma.Avisos;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

using Plataforma.Backoffice;

namespace Plataforma.Backoffice.Tablero;

/// <summary>
/// Lleva al tablero los eventos de la cola (letra 6.10). Todas las réplicas del Backoffice consumen de UNA cola
/// compartida: cada evento lo toma una sola réplica, y el backplane de Redis de SignalR lo hace llegar a todos
/// los navegadores del operador, estén conectados a la réplica que estén. Antes cada réplica tenía su cola y,
/// con el backplane, el navegador recibía cada evento una vez por réplica.
/// Si una réplica cae, la otra sigue consumiendo. El Backoffice recibe los datos del worker solo por la cola (letra 6.1).
/// </summary>
public sealed class TableroConsumidor : BackgroundService
{
    private static readonly TimeSpan EsperaReconexion = TimeSpan.FromSeconds(5);

    /// <summary>Cola compartida por todas las réplicas del Backoffice.</summary>
    public const string Cola = "plataforma.tablero";

    /// <summary>Un aviso del tablero que nadie mostró en este tiempo ya no sirve: se descarta.</summary>
    private const int VigenciaMensajeMs = 60_000;

    private readonly IHubContext<HubAvisosOperador> _hub;
    private readonly OpcionesRabbitMq _rabbit;
    private readonly ILogger<TableroConsumidor> _logger;

    public TableroConsumidor(IHubContext<HubAvisosOperador> hub, IOptions<OpcionesRabbitMq> rabbit, ILogger<TableroConsumidor> logger)
    {
        _hub = hub;
        _rabbit = rabbit.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_rabbit.Configurado)
        {
            _logger.LogWarning("Sin RabbitMq:Host: el tablero no recibe eventos en tiempo real.");
            return;
        }

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
                _logger.LogWarning(ex, "Tablero: sin conexión con RabbitMQ; reintento en {Segundos}s.", EsperaReconexion.TotalSeconds);
                await Task.Delay(EsperaReconexion, stoppingToken);
            }
        }
    }

    private async Task ConsumirAsync(CancellationToken stoppingToken)
    {
        await using var conexion = await TopologiaAvisos.CrearFabrica(_rabbit, $"plataforma-backoffice-tablero-{Environment.MachineName}")
            .CreateConnectionAsync(stoppingToken);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: stoppingToken);

        await canal.ExchangeDeclareAsync(_rabbit.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: stoppingToken);
        // Una cola para todas las réplicas (consumidores en competencia). Con vigencia: si el Backoffice
        // está apagado no se acumulan notificaciones viejas para mostrar al volver.
        await canal.QueueDeclareAsync(Cola, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-message-ttl"] = VigenciaMensajeMs },
            cancellationToken: stoppingToken);
        await canal.QueueBindAsync(Cola, _rabbit.Exchange, TopologiaAvisos.RoutingEstadoCambiado,
            cancellationToken: stoppingToken);
        await canal.QueueBindAsync(Cola, _rabbit.Exchange, TopologiaAvisos.RoutingResultadoAviso,
            cancellationToken: stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += async (_, mensaje) =>
        {
            try
            {
                await ReenviarAsync(mensaje, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Notificación efímera: si no se pudo mostrar, no se reintenta.
                _logger.LogWarning(ex, "Tablero: no se pudo reenviar el evento {RoutingKey}.", mensaje.RoutingKey);
            }
        };

        await canal.BasicConsumeAsync(Cola, autoAck: true, consumidor, stoppingToken);
        _logger.LogInformation("Tablero escuchando eventos en la cola {Cola}.", Cola);

        var cerrada = new TaskCompletionSource();
        conexion.ConnectionShutdownAsync += (_, _) =>
        {
            cerrada.TrySetResult();
            return Task.CompletedTask;
        };
        await cerrada.Task.WaitAsync(stoppingToken);
        throw new InvalidOperationException("Se cerró la conexión con RabbitMQ.");
    }

    private async Task ReenviarAsync(BasicDeliverEventArgs mensaje, CancellationToken ct)
    {
        switch (mensaje.RoutingKey)
        {
            case TopologiaAvisos.RoutingEstadoCambiado:
                var evento = JsonAvisos.Deserializar<EventoEstadoEnvio>(mensaje.Body.Span);
                if (evento is null || evento.OperadorId == Guid.Empty)
                    return;
                await _hub.Clients.Group(HubAvisosOperador.Grupo(evento.OperadorId)).SendAsync("estadoEnvioCambiado", new
                {
                    evento.EventId,
                    evento.EnvioId,
                    evento.EstadoAnterior,
                    evento.EstadoNuevo,
                    evento.Fecha
                }, ct);
                break;

            case TopologiaAvisos.RoutingResultadoAviso:
                var resultado = JsonAvisos.Deserializar<ResultadoAviso>(mensaje.Body.Span);
                if (resultado is null || resultado.OperadorId == Guid.Empty)
                    return;
                await _hub.Clients.Group(HubAvisosOperador.Grupo(resultado.OperadorId)).SendAsync("avisoResultado", new
                {
                    resultado.EntregaId,
                    resultado.EventId,
                    resultado.EnvioId,
                    resultado.TipoEvento,
                    resultado.Estado,
                    resultado.Intentos,
                    resultado.Error,
                    resultado.Fecha
                }, ct);
                break;
        }
    }
}
