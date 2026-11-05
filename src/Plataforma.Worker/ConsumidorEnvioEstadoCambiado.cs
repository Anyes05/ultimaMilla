using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Avisos;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Plataforma.Worker;

/// <summary>
/// Consume EnvioEstadoCambiado v1 y crea una EntregaAviso por cada suscripción del comercio que acepta
/// ese tipo de evento. Idempotente: si el mismo EventId llega dos veces (reintento del outbox, varias réplicas),
/// la clave única (EventId, SuscripcionId) impide crear un segundo aviso.
/// </summary>
public sealed class ConsumidorEnvioEstadoCambiado : ConsumidorRabbitBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IPublicadorAvisos _publicador;

    public ConsumidorEnvioEstadoCambiado(
        IServiceScopeFactory scopes,
        IPublicadorAvisos publicador,
        IOptions<OpcionesRabbitMq> rabbit,
        IOptions<OpcionesAvisos> avisos,
        ILogger<ConsumidorEnvioEstadoCambiado> logger)
        : base(rabbit, avisos, logger)
    {
        _scopes = scopes;
        _publicador = publicador;
    }

    protected override string Cola => TopologiaAvisos.ColaEventos;

    protected override async Task ProcesarAsync(IChannel canal, BasicDeliverEventArgs mensaje, CancellationToken stoppingToken)
    {
        EventoEstadoEnvio? evento = null;
        try
        {
            evento = JsonAvisos.Deserializar<EventoEstadoEnvio>(mensaje.Body.Span);
        }
        catch (System.Text.Json.JsonException ex)
        {
            Logger.LogError(ex, "Evento con JSON inválido (MessageId={MessageId}).", mensaje.BasicProperties.MessageId);
        }

        if (evento is null || evento.EventId == Guid.Empty || evento.OperadorId == Guid.Empty)
        {
            // No respeta el contrato: reencolarlo lo haría fallar para siempre.
            await canal.BasicRejectAsync(mensaje.DeliveryTag, requeue: false, stoppingToken);
            return;
        }

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AvisosDbContext>();

        var comercioId = evento.ComercioId ?? await BuscarComercioAsync(db, evento, stoppingToken);
        if (comercioId is null)
        {
            Logger.LogWarning("EventId={EventId}: no se encontró el comercio del envío {EnvioId}; no hay a quién avisar.",
                evento.EventId, evento.EnvioId);
            await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        var suscripciones = (await db.Suscripciones
                .Where(s => s.OperadorId == evento.OperadorId && s.ComercioId == comercioId && s.Activa)
                .ToListAsync(stoppingToken))
            .Where(s => s.Acepta(evento.EstadoNuevo))
            .ToList();

        // Cada suscripción recibe el cuerpo de SU versión del contrato (RF 36): el mismo evento sale en v1 para un
        // comercio y en v2 para otro. El número de seguimiento solo lo lleva v2; se busca si alguna lo necesita.
        var numeroSeguimiento = suscripciones.Any(s => s.VersionContrato >= ContratoAvisos.V2)
            ? await BuscarNumeroSeguimientoAsync(db, evento, stoppingToken)
            : null;

        var nuevas = new List<EntregaAviso>();
        foreach (var suscripcion in suscripciones)
        {
            var yaExiste = await db.Entregas.AnyAsync(
                e => e.EventId == evento.EventId && e.SuscripcionId == suscripcion.Id, stoppingToken);
            if (yaExiste)
            {
                Logger.LogInformation("EventId={EventId} duplicado para la suscripción {SuscripcionId}: se descarta.",
                    evento.EventId, suscripcion.Id);
                continue;
            }

            var payload = ContratoAvisos.Armar(suscripcion.VersionContrato, evento, numeroSeguimiento);
            var entrega = EntregaAviso.Crear(evento.EventId, suscripcion, evento.EnvioId, evento.EstadoNuevo, payload, DateTime.UtcNow);
            db.Entregas.Add(entrega);
            nuevas.Add(entrega);
        }

        if (nuevas.Count > 0)
        {
            try
            {
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (DbUpdateException ex) when (EsDuplicado(ex))
            {
                // Otra réplica ganó la carrera con el mismo EventId: ya existe, no se avisa dos veces.
                Logger.LogInformation("EventId={EventId} ya registrado por otra instancia: se descarta.", evento.EventId);
                await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            // Si esto falla, las entregas quedan Pendientes y el barrido las vuelve a encolar.
            foreach (var entrega in nuevas)
                await _publicador.PublicarEntregaAsync(entrega.Id, stoppingToken);
        }

        Logger.LogInformation(
            "EventId={EventId} Envio={EnvioId} {Anterior} -> {Nuevo}: {Cantidad} aviso(s) a encolar.",
            evento.EventId, evento.EnvioId, evento.EstadoAnterior, evento.EstadoNuevo, nuevas.Count);

        await canal.BasicAckAsync(mensaje.DeliveryTag, multiple: false, stoppingToken);
    }

    /// <summary>
    /// TRANSITORIO: el contrato v1 no trae comercioId, así que se lee de la tabla Envios del núcleo
    /// (solo lectura, filtrando por operador). Cuando el evento incluya comercioId se deja de usar.
    /// </summary>
    // Transitorio, como la búsqueda de comercio: el contrato v1 del evento interno no trae el número.
    private static async Task<string?> BuscarNumeroSeguimientoAsync(AvisosDbContext db, EventoEstadoEnvio evento, CancellationToken ct) =>
        await db.Database
            .SqlQuery<string>($"""SELECT "NumeroSeguimiento" AS "Value" FROM public."Envios" WHERE "Id" = {evento.EnvioId} AND "OperadorId" = {evento.OperadorId}""")
            .SingleOrDefaultAsync(ct);

    private static async Task<Guid?> BuscarComercioAsync(AvisosDbContext db, EventoEstadoEnvio evento, CancellationToken ct)
    {
        var comercio = await db.Database
            .SqlQuery<Guid>($"""SELECT "ComercioId" AS "Value" FROM public."Envios" WHERE "Id" = {evento.EnvioId} AND "OperadorId" = {evento.OperadorId}""")
            .SingleOrDefaultAsync(ct); // Id es la clave: a lo sumo una fila (y EF no advierte First sin OrderBy).
        return comercio == Guid.Empty ? null : comercio;
    }

    private static bool EsDuplicado(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };
}
