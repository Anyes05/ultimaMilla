using Microsoft.Extensions.Options;
using Plataforma.Avisos;
using Plataforma.HistorialEventos;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace Plataforma.Worker;

public sealed class ConsumidorHistorial : ConsumidorRabbitBase
{
    private readonly RepositorioHistorialMongo _repositorio;

    public ConsumidorHistorial(RepositorioHistorialMongo repositorio, IOptions<OpcionesRabbitMq> rabbit, IOptions<OpcionesAvisos> avisos, ILogger<ConsumidorHistorial> logger)
        : base(rabbit, avisos, logger) => _repositorio = repositorio;

    protected override string Cola => TopologiaHistorial.ColaEventos;
    protected override Task DeclararTopologiaAsync(IChannel canal, CancellationToken cancellationToken) => TopologiaHistorial.DeclararAsync(canal, Rabbit, Avisos, cancellationToken);
    protected override Task InicializarAsync(CancellationToken cancellationToken) => _repositorio.AsegurarIndicesAsync(cancellationToken);

    protected override async Task ProcesarAsync(IChannel canal, BasicDeliverEventArgs mensaje, CancellationToken stoppingToken)
    {
        EventoEstadoEnvio? evento;
        try { evento = JsonAvisos.Deserializar<EventoEstadoEnvio>(mensaje.Body.Span); }
        catch (System.Text.Json.JsonException) { evento = null; }
        if (evento is null || evento.EventId == Guid.Empty || evento.OperadorId == Guid.Empty || evento.EnvioId == Guid.Empty)
        {
            await PublicarAsync(canal, TopologiaHistorial.ColaFallidos, mensaje, 0, "Contrato inválido.", stoppingToken);
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
            return;
        }
        try
        {
            var insertado = await _repositorio.InsertarAsync(new EventoHistorialMongo(evento.EventId.ToString("D"), evento.OperadorId,
                evento.EnvioId, "EnvioEstadoCambiado", 1, evento.EstadoAnterior, evento.EstadoNuevo, evento.Fecha.UtcDateTime,
                DateTime.UtcNow, ObtenerTraceId(mensaje)), stoppingToken);
            Logger.LogInformation("Historial Mongo EventId={EventId}: {Resultado}.", evento.EventId, insertado ? "insertado" : "duplicado descartado");
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var intento = ObtenerIntento(mensaje) + 1;
            var destino = intento <= Avisos.MaximoReintentos ? TopologiaHistorial.ColaEspera(Avisos.Esperas[intento - 1]) : TopologiaHistorial.ColaFallidos;
            Logger.LogWarning(exception, "Historial Mongo EventId={EventId}: intento {Intento}, destino {Destino}.", evento.EventId, intento, destino);
            await PublicarAsync(canal, destino, mensaje, intento, exception.Message, stoppingToken);
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
        }
    }

    private static int ObtenerIntento(BasicDeliverEventArgs mensaje) =>
        int.TryParse(LeerHeader(mensaje, "x-intento"), out var intento) ? intento : 0;

    private static string? ObtenerTraceId(BasicDeliverEventArgs mensaje) => LeerHeader(mensaje, "traceparent");

    private static string? LeerHeader(BasicDeliverEventArgs mensaje, string nombre) =>
        mensaje.BasicProperties.Headers is not null && mensaje.BasicProperties.Headers.TryGetValue(nombre, out var valor)
            ? valor switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                _ => valor?.ToString()
            }
            : null;
    private static ValueTask PublicarAsync(IChannel canal, string cola, BasicDeliverEventArgs mensaje, int intento, string error, CancellationToken ct) =>
        canal.BasicPublishAsync(string.Empty, cola, mandatory: false, new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent,
            MessageId = mensaje.BasicProperties.MessageId, Headers = new Dictionary<string, object?> { ["x-intento"] = intento, ["x-ultimo-error"] = error } }, mensaje.Body, ct);
}