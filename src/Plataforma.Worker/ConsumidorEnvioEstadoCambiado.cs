using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Application.Mensajes;
using Plataforma.Domain;
using Plataforma.Infrastructure;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Plataforma.Worker;

/// <summary>
/// Consume EnvioEstadoCambiado v1 desde RabbitMQ.
/// Entrega avisos HTTP firmados de forma idempotente. Los fallos se reintentan
/// mediante colas TTL crecientes y terminan en DLQ junto con su registro técnico.
/// </summary>
public sealed class ConsumidorEnvioEstadoCambiado : BackgroundService
{
    private static readonly TimeSpan EsperaReconexion = TimeSpan.FromSeconds(5);
    private static readonly int[] EsperasReintentoSegundos = [5, 30, 120];

    private readonly OpcionesRabbitMqWorker _rabbit;
    private readonly OpcionesAvisos _avisos;
    private readonly IDbContextFactory<PlataformaDbContext> _contextos;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ConsumidorEnvioEstadoCambiado> _logger;

    public ConsumidorEnvioEstadoCambiado(
        IOptions<OpcionesRabbitMqWorker> rabbit,
        IOptions<OpcionesAvisos> avisos,
        IDbContextFactory<PlataformaDbContext> contextos,
        IHttpClientFactory httpClientFactory,
        ILogger<ConsumidorEnvioEstadoCambiado> logger)
    {
        _rabbit = rabbit.Value;
        _avisos = avisos.Value;
        _contextos = contextos;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_rabbit.Host) ||
            string.IsNullOrWhiteSpace(_avisos.ReceptorUrl) ||
            string.IsNullOrWhiteSpace(_avisos.ClaveFirma))
        {
            _logger.LogError("Falta configuración RabbitMQ o Avisos; el worker no puede consumir mensajes.");
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
        await DeclararTopologiaAsync(canal, stoppingToken);
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

            var intento = ObtenerIntento(entrega.BasicProperties.Headers);
            try
            {
                if (await YaProcesadoAsync(evento.EventId, stoppingToken))
                {
                    _logger.LogInformation("Aviso duplicado EventId={EventId}; se confirma sin reenviar.", evento.EventId);
                }
                else
                {
                    await EntregarAvisoAsync(evento, json, stoppingToken);
                    _logger.LogInformation("Aviso entregado EventId={EventId} Operador={OperadorId}.", evento.EventId, evento.OperadorId);
                }

                await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, stoppingToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await ManejarFalloAsync(canal, entrega, evento, json, intento, ex, stoppingToken);
            }
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

    private async Task DeclararTopologiaAsync(IChannel canal, CancellationToken cancellationToken)
    {
        const string intercambioRetry = "plataforma.avisos.retry";
        const string intercambioDlq = "plataforma.avisos.dlq";

        await canal.ExchangeDeclareAsync(intercambioRetry, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);
        await canal.ExchangeDeclareAsync(intercambioDlq, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);
        await canal.QueueDeclareAsync("plataforma.avisos-comercio.dlq", durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await canal.QueueBindAsync("plataforma.avisos-comercio.dlq", intercambioDlq, "fallido", cancellationToken: cancellationToken);

        for (var indice = 0; indice < EsperasReintentoSegundos.Length; indice++)
        {
            var cola = $"plataforma.avisos-comercio.retry.{indice + 1}";
            var argumentos = new Dictionary<string, object?>
            {
                ["x-message-ttl"] = EsperasReintentoSegundos[indice] * 1000,
                ["x-dead-letter-exchange"] = _rabbit.Exchange,
                ["x-dead-letter-routing-key"] = _avisos.RoutingKey
            };
            await canal.QueueDeclareAsync(cola, durable: true, exclusive: false, autoDelete: false,
                arguments: argumentos, cancellationToken: cancellationToken);
            await canal.QueueBindAsync(cola, intercambioRetry, $"retry.{indice + 1}", cancellationToken: cancellationToken);
        }
    }

    private async Task<bool> YaProcesadoAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await using var contexto = await _contextos.CreateDbContextAsync(cancellationToken);
        return await contexto.EventosAvisoProcesados.AnyAsync(evento => evento.EventId == eventId, cancellationToken);
    }

    private async Task EntregarAvisoAsync(EnvioEstadoCambiado evento, string json, CancellationToken cancellationToken)
    {
        using var contenido = new StringContent(json, Encoding.UTF8, "application/json");
        var firma = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_avisos.ClaveFirma), Encoding.UTF8.GetBytes(json)));
        contenido.Headers.Add("X-Plataforma-Firma", firma);
        contenido.Headers.Add("X-Plataforma-EventId", evento.EventId.ToString());
        var cliente = _httpClientFactory.CreateClient("receptor-avisos");
        using var respuesta = await cliente.PostAsync(_avisos.ReceptorUrl, contenido, cancellationToken);
        respuesta.EnsureSuccessStatusCode();

        await using var contexto = await _contextos.CreateDbContextAsync(cancellationToken);
        if (!await contexto.EventosAvisoProcesados.AnyAsync(item => item.EventId == evento.EventId, cancellationToken))
            contexto.EventosAvisoProcesados.Add(EventoAvisoProcesado.Registrar(evento.EventId, evento.OperadorId, evento.EnvioId));
        var fallido = await contexto.AvisosFallidos.SingleOrDefaultAsync(item => item.EventId == evento.EventId, cancellationToken);
        fallido?.MarcarResuelto();
        await contexto.SaveChangesAsync(cancellationToken);
    }

    private async Task ManejarFalloAsync(
        IChannel canal,
        BasicDeliverEventArgs entrega,
        EnvioEstadoCambiado evento,
        string json,
        int intentoActual,
        Exception error,
        CancellationToken cancellationToken)
    {
        var siguienteIntento = intentoActual + 1;
        if (siguienteIntento < _avisos.MaximoIntentos)
        {
            await PublicarAsync(canal, "plataforma.avisos.retry", $"retry.{siguienteIntento}", entrega.BasicProperties, json,
                siguienteIntento, cancellationToken);
            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, cancellationToken);
            _logger.LogWarning(error, "Aviso EventId={EventId} falló; retry {Intento}/{Maximo}.",
                evento.EventId, siguienteIntento, _avisos.MaximoIntentos);
            return;
        }

        await RegistrarFallidoAsync(evento, json, error.Message, siguienteIntento, cancellationToken);
        await PublicarAsync(canal, "plataforma.avisos.dlq", "fallido", entrega.BasicProperties, json,
            siguienteIntento, cancellationToken);
        await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, cancellationToken);
        _logger.LogError(error, "Aviso EventId={EventId} enviado a DLQ después de {Intentos} intentos.", evento.EventId, siguienteIntento);
    }

    private static async Task PublicarAsync(IChannel canal, string exchange, string routingKey, IReadOnlyBasicProperties origen,
        string json, int intento, CancellationToken cancellationToken)
    {
        var propiedades = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = origen.MessageId,
            Type = origen.Type,
            Headers = origen.Headers is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(origen.Headers)
        };
        propiedades.Headers["x-intento-aviso"] = intento;
        await canal.BasicPublishAsync(exchange, routingKey, mandatory: false, propiedades,
            Encoding.UTF8.GetBytes(json), cancellationToken);
    }

    private async Task RegistrarFallidoAsync(EnvioEstadoCambiado evento, string json, string error, int intentos, CancellationToken cancellationToken)
    {
        await using var contexto = await _contextos.CreateDbContextAsync(cancellationToken);
        var fallido = await contexto.AvisosFallidos.SingleOrDefaultAsync(aviso => aviso.EventId == evento.EventId, cancellationToken);
        if (fallido is null)
            contexto.AvisosFallidos.Add(AvisoFallido.Registrar(evento.EventId, evento.OperadorId, evento.EnvioId, json, error, intentos));
        else
            fallido.Actualizar(error, intentos);
        await contexto.SaveChangesAsync(cancellationToken);
    }

    private static int ObtenerIntento(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-intento-aviso", out var valor))
            return 0;
        return valor switch
        {
            int numero => numero,
            long numero => checked((int)numero),
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var numero) => numero,
            _ => 0
        };
    }
}
