using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Plataforma.Avisos;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

using Plataforma.Backoffice;

namespace Plataforma.Backoffice.Tablero;

/// <summary>
/// Lleva al tablero los eventos de la cola. Cada réplica del Backoffice declara SU PROPIA cola
/// (exclusiva y temporal) enlazada al exchange: todas reciben todos los eventos y cada una los empuja
/// a los navegadores conectados a ella. Así el tablero funciona con varias instancias sin backplane (letra 6.10),
/// y el Backoffice recibe la información del worker solo por la cola (letra 6.1).
/// Es intencionalmente por instancia: no es una tarea programada que deba correr una sola vez.
/// </summary>
public sealed class TableroConsumidor : BackgroundService
{
    private static readonly TimeSpan EsperaReconexion = TimeSpan.FromSeconds(5);

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
        // Cola con nombre del servidor, exclusiva y autodelete: desaparece cuando se apaga la réplica.
        var cola = await canal.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true,
            cancellationToken: stoppingToken);
        await canal.QueueBindAsync(cola.QueueName, _rabbit.Exchange, TopologiaAvisos.RoutingEstadoCambiado,
            cancellationToken: stoppingToken);
        await canal.QueueBindAsync(cola.QueueName, _rabbit.Exchange, TopologiaAvisos.RoutingResultadoAviso,
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

        await canal.BasicConsumeAsync(cola.QueueName, autoAck: true, consumidor, stoppingToken);
        _logger.LogInformation("Tablero escuchando eventos en la cola {Cola}.", cola.QueueName);

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
