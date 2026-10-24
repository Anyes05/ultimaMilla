using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Plataforma.Avisos;

public interface IPublicadorAvisos
{
    /// <summary>Encola una entrega para que el worker la envíe (reintento manual o barrido de pendientes).</summary>
    Task PublicarEntregaAsync(Guid entregaId, CancellationToken cancellationToken = default);

    /// <summary>Publica el resultado de un aviso para el tablero en tiempo real.</summary>
    Task PublicarResultadoAsync(ResultadoAviso resultado, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publicador con una conexión perezosa y reutilizada. Lo usan el worker y las webs:
/// Backoffice y Portal se comunican con el worker solo a través de la cola (letra 6.1).
/// </summary>
public sealed class PublicadorRabbitAvisos : IPublicadorAvisos, IAsyncDisposable
{
    private readonly OpcionesRabbitMq _rabbit;
    private readonly OpcionesAvisos _avisos;
    private readonly ILogger<PublicadorRabbitAvisos> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _conexion;
    private IChannel? _canal;

    public PublicadorRabbitAvisos(
        IOptions<OpcionesRabbitMq> rabbit,
        IOptions<OpcionesAvisos> avisos,
        ILogger<PublicadorRabbitAvisos> logger)
    {
        _rabbit = rabbit.Value;
        _avisos = avisos.Value;
        _logger = logger;
    }

    public Task PublicarEntregaAsync(Guid entregaId, CancellationToken cancellationToken = default) =>
        PublicarAsync(string.Empty, TopologiaAvisos.ColaEntregas, JsonAvisos.Serializar(new MensajeEntrega(entregaId)),
            entregaId.ToString(), cancellationToken);

    public Task PublicarResultadoAsync(ResultadoAviso resultado, CancellationToken cancellationToken = default) =>
        PublicarAsync(_rabbit.Exchange, TopologiaAvisos.RoutingResultadoAviso, JsonAvisos.Serializar(resultado),
            resultado.EntregaId.ToString(), cancellationToken);

    private async Task PublicarAsync(
        string exchange,
        string routingKey,
        string json,
        string messageId,
        CancellationToken cancellationToken)
    {
        if (!_rabbit.Configurado)
            throw new InvalidOperationException("RabbitMQ no está configurado (RabbitMq:Host).");

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var canal = await ObtenerCanalAsync(cancellationToken);
            await canal.BasicPublishAsync(
                exchange,
                routingKey,
                mandatory: false,
                basicProperties: new BasicProperties
                {
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                    MessageId = messageId
                },
                body: System.Text.Encoding.UTF8.GetBytes(json),
                cancellationToken: cancellationToken);
        }
        catch
        {
            // Conexión rota: se descarta para reconectar en el próximo intento.
            await CerrarAsync();
            throw;
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

        await CerrarAsync();
        _conexion = await TopologiaAvisos.CrearFabrica(_rabbit, "plataforma-avisos-publicador")
            .CreateConnectionAsync(cancellationToken);
        _canal = await _conexion.CreateChannelAsync(cancellationToken: cancellationToken);
        await TopologiaAvisos.DeclararAsync(_canal, _rabbit, _avisos, cancellationToken);
        _logger.LogInformation("Publicador de avisos conectado a RabbitMQ {Host}.", _rabbit.Host);
        return _canal;
    }

    private async Task CerrarAsync()
    {
        try
        {
            if (_canal is not null)
                await _canal.DisposeAsync();
            if (_conexion is not null)
                await _conexion.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error al cerrar la conexión del publicador de avisos.");
        }
        finally
        {
            _canal = null;
            _conexion = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CerrarAsync();
        _lock.Dispose();
    }
}
