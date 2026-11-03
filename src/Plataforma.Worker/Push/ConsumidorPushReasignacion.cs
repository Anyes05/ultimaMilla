using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Avisos;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Plataforma.Worker.Push;

/// <summary>
/// Push de reasignación urgente (opcional 7.3): cuando un envío se reasigna, avisa al repartidor nuevo en cada
/// dispositivo registrado, también con la app cerrada (eso lo resuelve FCM, del lado de Álvaro).
/// <list type="bullet">
/// <item>Idempotente: (EventId, token) es único en avisos."NotificacionesPush"; el mismo evento repetido no
/// llega dos veces al mismo teléfono (criterio del opcional y letra 6.8).</item>
/// <item>Si el proveedor falla de forma transitoria, el mensaje vuelve con espera creciente y solo se reintentan
/// los dispositivos pendientes; agotados los reintentos, copia en la cola de fallidos.</item>
/// <item>Token inválido: se marca y no se reintenta. La baja del dispositivo está pendiente de decidir
/// (worker con núcleo o evento dispositivoinvalido.v1, ver la propuesta).</item>
/// </list>
/// </summary>
public sealed class ConsumidorPushReasignacion : ConsumidorRabbitBase
{
    private static readonly Meter Medidor = new("Plataforma.Push");
    private static readonly Counter<long> Envios = Medidor.CreateCounter<long>(
        "plataforma.push.envios", unit: "{push}", description: "Push de reasignación por resultado (enviado, token-invalido, reintento, fallido).");

    private readonly IServiceScopeFactory _scopes;
    private readonly IEmisorPush _emisor;

    public ConsumidorPushReasignacion(
        IServiceScopeFactory scopes,
        IEmisorPush emisor,
        IOptions<OpcionesRabbitMq> rabbit,
        IOptions<OpcionesAvisos> avisos,
        ILogger<ConsumidorPushReasignacion> logger)
        : base(rabbit, avisos, logger)
    {
        _scopes = scopes;
        _emisor = emisor;
    }

    protected override string Cola => TopologiaPush.ColaEventos;

    protected override Task DeclararTopologiaAsync(IChannel canal, CancellationToken cancellationToken) =>
        TopologiaPush.DeclararAsync(canal, Rabbit, Avisos, cancellationToken);

    protected override async Task ProcesarAsync(IChannel canal, BasicDeliverEventArgs mensaje, CancellationToken stoppingToken)
    {
        EventoEnvioReasignado? evento;
        try { evento = JsonAvisos.Deserializar<EventoEnvioReasignado>(mensaje.Body.Span); }
        catch (System.Text.Json.JsonException) { evento = null; }

        if (evento is null || evento.EventId == Guid.Empty || evento.OperadorId == Guid.Empty || evento.RepartidorNuevoId == Guid.Empty)
        {
            // No respeta el contrato: reintentarlo lo haría fallar siempre.
            await PublicarAsync(canal, TopologiaPush.ColaFallidos, mensaje, 0, "Contrato inválido.", stoppingToken);
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AvisosDbContext>();

        var dispositivos = await BuscarDispositivosAsync(db, evento, stoppingToken);
        if (dispositivos.Count == 0)
        {
            Logger.LogInformation("Push EventId={EventId}: el repartidor {RepartidorId} no tiene dispositivos registrados.",
                evento.EventId, evento.RepartidorNuevoId);
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        var notificaciones = await RegistrarAsync(db, evento, dispositivos, stoppingToken);
        var maximo = Avisos.MaximoReintentos;
        var ahora = DateTime.UtcNow;
        var quedanReintentos = false;
        var agotadas = false;

        foreach (var notificacion in notificaciones.Where(n => n.PorEnviar))
        {
            var resultado = await EnviarAsync(evento, notificacion, stoppingToken);
            switch (resultado)
            {
                case ResultadoPush.Enviado:
                    notificacion.MarcarEnviada(ahora);
                    Envios.Add(1, new KeyValuePair<string, object?>("resultado", "enviado"));
                    break;
                case ResultadoPush.TokenInvalido:
                    notificacion.MarcarTokenInvalido("El proveedor rechazó el token del dispositivo.", ahora);
                    Envios.Add(1, new KeyValuePair<string, object?>("resultado", "token-invalido"));
                    break;
                default:
                    var quedan = notificacion.RegistrarFallo("Error transitorio del proveedor de push.", maximo, ahora);
                    quedanReintentos |= quedan;
                    agotadas |= !quedan;
                    Envios.Add(1, new KeyValuePair<string, object?>("resultado", quedan ? "reintento" : "fallido"));
                    break;
            }
        }

        await db.SaveChangesAsync(stoppingToken);

        if (quedanReintentos)
        {
            var intento = Math.Min(LeerIntento(mensaje) + 1, Avisos.Esperas.Count);
            await PublicarAsync(canal, TopologiaPush.ColaEspera(Avisos.Esperas[intento - 1]), mensaje, intento,
                "Push pendiente en algún dispositivo.", stoppingToken);
        }
        else if (agotadas)
        {
            await PublicarAsync(canal, TopologiaPush.ColaFallidos, mensaje, LeerIntento(mensaje), "Reintentos agotados.", stoppingToken);
        }

        Logger.LogInformation(
            "Push EventId={EventId} envío {EnvioId} → repartidor {RepartidorId}: {Enviadas} enviada(s), {Pendientes} pendiente(s), {Cerradas} sin enviar.",
            evento.EventId, evento.EnvioId, evento.RepartidorNuevoId,
            notificaciones.Count(n => n.Estado == EstadoNotificacionPush.Enviada),
            notificaciones.Count(n => n.PorEnviar),
            notificaciones.Count(n => n.Estado is EstadoNotificacionPush.TokenInvalido or EstadoNotificacionPush.Fallida));

        await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
    }

    private async Task<ResultadoPush> EnviarAsync(EventoEnvioReasignado evento, NotificacionPush notificacion, CancellationToken ct)
    {
        var numero = string.IsNullOrWhiteSpace(evento.NumeroSeguimiento) ? "un envío" : $"el envío {evento.NumeroSeguimiento}";
        var datos = new Dictionary<string, string>
        {
            ["tipo"] = "reasignacion",
            ["envioId"] = evento.EnvioId.ToString(),
            ["rutaId"] = evento.RutaNuevaId?.ToString() ?? string.Empty
        };
        try
        {
            return await _emisor.EnviarAsync(new MensajePush(
                notificacion.Token, notificacion.Plataforma,
                evento.Urgente ? "Reasignación urgente" : "Nueva asignación",
                $"Se te asignó {numero}. Abrí la app para ver la ruta actualizada.",
                datos), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Push EventId={EventId}: error del emisor; se reintenta.", evento.EventId);
            return ResultadoPush.ErrorTransitorio;
        }
    }

    /// <summary>
    /// Crea (una sola vez por dispositivo) los registros de idempotencia. Si otra copia del mensaje los creó
    /// a la vez, el índice único lo impide y se releen.
    /// </summary>
    private static async Task<List<NotificacionPush>> RegistrarAsync(
        AvisosDbContext db, EventoEnvioReasignado evento, IReadOnlyList<DispositivoDestino> dispositivos, CancellationToken ct)
    {
        var existentes = await db.NotificacionesPush.Where(n => n.EventId == evento.EventId).ToListAsync(ct);
        var ahora = DateTime.UtcNow;
        foreach (var dispositivo in dispositivos.Where(d => existentes.All(e => e.Token != d.Token)))
        {
            var nueva = NotificacionPush.Crear(evento.EventId, evento.OperadorId, evento.RepartidorNuevoId, evento.EnvioId,
                dispositivo.Token, dispositivo.Plataforma, ahora);
            db.NotificacionesPush.Add(nueva);
            existentes.Add(nueva);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existentes = await db.NotificacionesPush.Where(n => n.EventId == evento.EventId).ToListAsync(ct);
        }

        return existentes;
    }

    // Transitorio, como la búsqueda de comercio de avisos: los tokens viven en el núcleo y el worker no lo referencia.
    private static async Task<IReadOnlyList<DispositivoDestino>> BuscarDispositivosAsync(
        AvisosDbContext db, EventoEnvioReasignado evento, CancellationToken ct) =>
        await db.Database.SqlQuery<DispositivoDestino>($"""
            SELECT "Token", "Plataforma" FROM public."DispositivosRepartidor"
            WHERE "OperadorId" = {evento.OperadorId} AND "RepartidorId" = {evento.RepartidorNuevoId}
            """).ToListAsync(ct);

    private static int LeerIntento(BasicDeliverEventArgs mensaje) =>
        mensaje.BasicProperties.Headers is { } headers && headers.TryGetValue("x-intento", out var valor) &&
        int.TryParse(valor is byte[] bytes ? Encoding.UTF8.GetString(bytes) : valor?.ToString(), out var intento)
            ? intento
            : 0;

    private static ValueTask PublicarAsync(IChannel canal, string cola, BasicDeliverEventArgs mensaje, int intento, string error, CancellationToken ct) =>
        canal.BasicPublishAsync(string.Empty, cola, mandatory: false,
            new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = mensaje.BasicProperties.MessageId,
                Headers = new Dictionary<string, object?> { ["x-intento"] = intento, ["x-ultimo-error"] = error }
            },
            mensaje.Body, ct);

    private sealed class DispositivoDestino
    {
        public string Token { get; set; } = string.Empty;
        public string Plataforma { get; set; } = string.Empty;
    }
}
