using System.Diagnostics.Metrics;
using System.Net.Mail;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Avisos;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Plataforma.Worker.Notificaciones;

/// <summary>
/// Notifica al destinatario los cambios de estado relevantes de su envío (RF 27), por correo y con su enlace de seguimiento.
/// Idempotente por EventId (letra 6.8); si el correo falla, reintenta con espera creciente y, agotados los reintentos,
/// queda fallida para que el administrador la reintente. El email es opcional: sin email no hay nada que enviar.
/// </summary>
public sealed class ConsumidorNotificacionDestinatario : ConsumidorRabbitBase
{
    private static readonly Meter Medidor = new("Plataforma.Notificaciones");
    private static readonly Counter<long> Envios = Medidor.CreateCounter<long>(
        "plataforma.notificaciones.destinatario", unit: "{correo}", description: "Notificaciones al destinatario por resultado (enviada, reintento, fallida).");

    private readonly IServiceScopeFactory _scopes;
    private readonly IEmisorCorreo _emisor;
    private readonly OpcionesNotificaciones _notificaciones;

    public ConsumidorNotificacionDestinatario(
        IServiceScopeFactory scopes,
        IEmisorCorreo emisor,
        IOptions<OpcionesNotificaciones> notificaciones,
        IOptions<OpcionesRabbitMq> rabbit,
        IOptions<OpcionesAvisos> avisos,
        ILogger<ConsumidorNotificacionDestinatario> logger)
        : base(rabbit, avisos, logger)
    {
        _scopes = scopes;
        _emisor = emisor;
        _notificaciones = notificaciones.Value;
    }

    protected override string Cola => TopologiaNotificaciones.ColaEventos;

    protected override Task DeclararTopologiaAsync(IChannel canal, CancellationToken cancellationToken) =>
        TopologiaNotificaciones.DeclararAsync(canal, Rabbit, Avisos, cancellationToken);

    protected override async Task ProcesarAsync(IChannel canal, BasicDeliverEventArgs mensaje, CancellationToken stoppingToken)
    {
        EventoEstadoEnvio? evento;
        try { evento = JsonAvisos.Deserializar<EventoEstadoEnvio>(mensaje.Body.Span); }
        catch (System.Text.Json.JsonException) { evento = null; }

        if (evento is null || evento.EventId == Guid.Empty || evento.OperadorId == Guid.Empty || evento.EnvioId == Guid.Empty)
        {
            // No respeta el contrato: reintentarlo lo haría fallar siempre.
            await PublicarAsync(canal, TopologiaNotificaciones.ColaFallidos, mensaje, 0, "Contrato inválido.", stoppingToken);
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        if (!PlantillaNotificacion.EsRelevante(evento.EstadoNuevo))
        {
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AvisosDbContext>();

        var datos = await BuscarDatosAsync(db, evento, stoppingToken);
        if (datos is null || !MailAddress.TryCreate(datos.Email, out _))
        {
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        var notificacion = await RegistrarAsync(db, evento, Encoding.UTF8.GetString(mensaje.Body.Span), stoppingToken);
        if (!notificacion.PorEnviar)
        {
            Logger.LogInformation("Notificación EventId={EventId}: ya procesada.", evento.EventId);
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        var error = await EnviarAsync(evento, datos, stoppingToken);
        var ahora = DateTime.UtcNow;
        var quedanReintentos = false;
        if (error is null)
        {
            notificacion.MarcarEnviada(ahora);
            Envios.Add(1, new KeyValuePair<string, object?>("resultado", "enviada"));
        }
        else
        {
            quedanReintentos = notificacion.RegistrarFallo(error, Avisos.MaximoReintentos, ahora);
            Envios.Add(1, new KeyValuePair<string, object?>("resultado", quedanReintentos ? "reintento" : "fallida"));
        }

        await db.SaveChangesAsync(stoppingToken);

        if (quedanReintentos)
        {
            var intento = Math.Min(LeerIntento(mensaje) + 1, Avisos.Esperas.Count);
            await PublicarAsync(canal, TopologiaNotificaciones.ColaEspera(Avisos.Esperas[intento - 1]), mensaje, intento, error!, stoppingToken);
        }
        else if (error is not null)
        {
            await PublicarAsync(canal, TopologiaNotificaciones.ColaFallidos, mensaje, LeerIntento(mensaje), "Reintentos agotados.", stoppingToken);
        }

        Logger.LogInformation("Notificación EventId={EventId} envío {EnvioId} ({Estado}): {Resultado}, intento {Intentos}.",
            evento.EventId, evento.EnvioId, evento.EstadoNuevo, notificacion.Estado, notificacion.Intentos);

        await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
    }

    /// <summary>Devuelve el error, sin la dirección del destinatario (queda guardado y se ve en el Backoffice).</summary>
    private async Task<string?> EnviarAsync(EventoEstadoEnvio evento, DatosEnvio datos, CancellationToken ct)
    {
        var correo = PlantillaNotificacion.Armar(
            new DatosNotificacion(datos.Operador, datos.ColorMarca, datos.NumeroSeguimiento, datos.Token, evento.EstadoNuevo),
            _notificaciones.UrlSeguimiento);
        try
        {
            await _emisor.EnviarAsync(datos.Email, datos.Operador, correo, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning("Notificación EventId={EventId}: no se pudo enviar ({Error}).", evento.EventId, ex.GetType().Name);
            return ex is SmtpException { StatusCode: not SmtpStatusCode.GeneralFailure } smtp
                ? $"El servidor de correo respondió {smtp.StatusCode}."
                : "No se pudo conectar con el servidor de correo.";
        }
    }

    /// <summary>Crea el registro de idempotencia una sola vez. Si otra copia del mensaje lo creó a la vez, se relee.</summary>
    private static async Task<NotificacionDestinatario> RegistrarAsync(
        AvisosDbContext db, EventoEstadoEnvio evento, string eventoJson, CancellationToken ct)
    {
        var existente = await db.NotificacionesDestinatario.FirstOrDefaultAsync(n => n.EventId == evento.EventId, ct);
        if (existente is not null)
            return existente;

        var nueva = NotificacionDestinatario.Crear(evento, eventoJson, DateTime.UtcNow);
        db.NotificacionesDestinatario.Add(nueva);
        try
        {
            await db.SaveChangesAsync(ct);
            return nueva;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await db.NotificacionesDestinatario.SingleAsync(n => n.EventId == evento.EventId, ct);
        }
    }

    // Transitorio, como la búsqueda de comercio de avisos: el contrato v1 del evento no trae estos datos.
    private static async Task<DatosEnvio?> BuscarDatosAsync(AvisosDbContext db, EventoEstadoEnvio evento, CancellationToken ct) =>
        (await db.Database.SqlQuery<DatosEnvio>($"""
            SELECT e."EmailDestinatario" AS "Email", e."TokenSeguimientoPublico" AS "Token", e."NumeroSeguimiento",
                   o."Nombre" AS "Operador", o."ColorMarca"
            FROM public."Envios" e JOIN public."Operadores" o ON o."Id" = e."OperadorId"
            WHERE e."Id" = {evento.EnvioId} AND e."OperadorId" = {evento.OperadorId}
            """).ToListAsync(ct)).SingleOrDefault();

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

    private sealed class DatosEnvio
    {
        public string Email { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string NumeroSeguimiento { get; set; } = string.Empty;
        public string Operador { get; set; } = string.Empty;
        public string ColorMarca { get; set; } = string.Empty;
    }
}
