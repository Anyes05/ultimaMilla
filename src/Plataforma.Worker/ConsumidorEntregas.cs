using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Avisos;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Plataforma.Worker;

/// <summary>
/// Envía cada aviso al sistema del comercio por HTTP, firmado con HMAC (letra 6.9).
/// La base es la fuente de verdad del estado; la cola solo dice "hay que intentar esta entrega".
///   - 2xx: Entregado.
///   - fallo con reintentos disponibles: se manda a la cola de espera que corresponde (5 s, 15 s, 45 s...).
///   - fallo sin reintentos: Fallido y copia en la cola de fallidos (DLQ); se reintenta a mano.
/// Antes del HTTP la entrega se "toma" con concurrencia optimista: dos copias del mismo mensaje
/// no llaman dos veces al comercio.
/// </summary>
public sealed class ConsumidorEntregas : ConsumidorRabbitBase
{
    public const string ClienteHttp = "avisos-comercio";

    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _http;
    private readonly IPublicadorAvisos _publicador;

    public ConsumidorEntregas(
        IServiceScopeFactory scopes,
        IHttpClientFactory http,
        IPublicadorAvisos publicador,
        IOptions<OpcionesRabbitMq> rabbit,
        IOptions<OpcionesAvisos> avisos,
        ILogger<ConsumidorEntregas> logger)
        : base(rabbit, avisos, logger)
    {
        _scopes = scopes;
        _http = http;
        _publicador = publicador;
    }

    protected override string Cola => TopologiaAvisos.ColaEntregas;

    protected override async Task ProcesarAsync(IChannel canal, BasicDeliverEventArgs mensaje, CancellationToken stoppingToken)
    {
        var pedido = JsonAvisos.Deserializar<MensajeEntrega>(mensaje.Body.Span);
        if (pedido is null || pedido.EntregaId == Guid.Empty)
        {
            await canal.BasicRejectAsync(mensaje.DeliveryTag, requeue: false, stoppingToken);
            return;
        }

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AvisosDbContext>();

        var entrega = await db.Entregas.FirstOrDefaultAsync(e => e.Id == pedido.EntregaId, stoppingToken);
        if (entrega is null || !entrega.PuedeTomarse(DateTime.UtcNow))
        {
            // Ya entregada, fallida o en curso por otra copia: procesarla de nuevo duplicaría el aviso.
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        entrega.Tomar(DateTime.UtcNow);
        try
        {
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            Logger.LogInformation("Entrega {EntregaId} tomada por otra copia: se descarta.", entrega.Id);
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        var suscripcion = await db.Suscripciones.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == entrega.SuscripcionId, stoppingToken);

        var (exito, codigo, error) = suscripcion switch
        {
            null => (false, (int?)null, "La suscripción ya no existe."),
            { Activa: false } => (false, null, "La suscripción está desactivada."),
            _ when !Avisos.UrlPermitida(suscripcion.Url) => (false, null, "La URL no está permitida (se exige https)."),
            _ => await EnviarAsync(entrega, suscripcion, stoppingToken)
        };

        var ahora = DateTime.UtcNow;
        string? colaDestino = null;
        if (exito)
        {
            entrega.MarcarEntregada(codigo ?? 200, ahora);
        }
        else
        {
            // Sin suscripción válida no tiene sentido esperar: se agota de inmediato.
            var maximo = suscripcion is { Activa: true } ? Avisos.MaximoReintentos : 0;
            var quedan = entrega.RegistrarFallo(error ?? "Error desconocido", codigo, maximo, ahora);
            colaDestino = quedan
                ? TopologiaAvisos.ColaEspera(Avisos.Esperas[entrega.Intentos - 1])
                : TopologiaAvisos.ColaFallidos;
        }

        await db.SaveChangesAsync(stoppingToken);
        MetricasAvisos.RegistrarIntento(exito ? "entregado"
            : colaDestino == TopologiaAvisos.ColaFallidos ? "fallido"
            : "reintento");

        if (colaDestino is not null)
        {
            await canal.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: colaDestino,
                mandatory: false,
                basicProperties: new BasicProperties
                {
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                    MessageId = entrega.Id.ToString(),
                    Headers = new Dictionary<string, object?>
                    {
                        ["x-intento"] = entrega.Intentos,
                        ["x-ultimo-error"] = entrega.UltimoError
                    }
                },
                body: mensaje.Body,
                cancellationToken: stoppingToken);
        }

        Logger.LogInformation(
            "Aviso {EntregaId} EventId={EventId} intento {Intento}: {Estado} (HTTP {Codigo}) {Destino}",
            entrega.Id, entrega.EventId, entrega.Intentos, entrega.Estado, codigo, colaDestino ?? string.Empty);

        await PublicarResultadoAsync(entrega, stoppingToken);
        await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
    }

    private async Task<(bool Exito, int? Codigo, string? Error)> EnviarAsync(
        EntregaAviso entrega,
        SuscripcionAviso suscripcion,
        CancellationToken stoppingToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var pedido = new HttpRequestMessage(HttpMethod.Post, suscripcion.Url)
        {
            Content = new StringContent(entrega.Payload, Encoding.UTF8, "application/json")
        };
        pedido.Headers.Add(FirmaAviso.HeaderEvento, entrega.EventId.ToString());
        pedido.Headers.Add(FirmaAviso.HeaderTimestamp, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        pedido.Headers.Add(FirmaAviso.HeaderFirma, FirmaAviso.Firmar(suscripcion.Secreto, timestamp, entrega.Payload));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Avisos.TimeoutHttpSegundos));
        var inicio = System.Diagnostics.Stopwatch.GetTimestamp();
        int? codigoHttp = null;
        try
        {
            using var respuesta = await _http.CreateClient(ClienteHttp).SendAsync(pedido, timeout.Token);
            var codigo = (int)respuesta.StatusCode;
            codigoHttp = codigo;
            return respuesta.IsSuccessStatusCode
                ? (true, codigo, null)
                : (false, codigo, $"El receptor respondió HTTP {codigo}.");
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return (false, null, $"Sin respuesta del receptor en {Avisos.TimeoutHttpSegundos} s.");
        }
        catch (HttpRequestException ex)
        {
            return (false, null, $"No se pudo conectar con el receptor: {ex.Message}");
        }
        finally
        {
            MetricasAvisos.RegistrarDuracionHttp(System.Diagnostics.Stopwatch.GetElapsedTime(inicio), codigoHttp);
        }
    }

    private async Task PublicarResultadoAsync(EntregaAviso entrega, CancellationToken stoppingToken)
    {
        try
        {
            await _publicador.PublicarResultadoAsync(new ResultadoAviso(
                entrega.Id, entrega.EventId, entrega.OperadorId, entrega.ComercioId, entrega.EnvioId,
                entrega.TipoEvento, entrega.Estado.ToString(), entrega.Intentos, entrega.UltimoError,
                DateTimeOffset.UtcNow), stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El tablero es informativo: no frena la entrega si no se pudo notificar.
            Logger.LogWarning(ex, "No se pudo publicar el resultado del aviso {EntregaId} para el tablero.", entrega.Id);
        }
    }
}
