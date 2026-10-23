namespace Plataforma.Domain;

/// <summary>
/// Fila de outbox: se escribe en la misma transacción que el cambio de dominio.
/// El worker (posterior) lee pendientes y publica a la cola.
/// </summary>
public class MensajeOutbox
{
    public const string TipoEnvioEstadoCambiado = "EnvioEstadoCambiado";
    public const string VersionContratoV1 = "1";

    public Guid EventId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string Tipo { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset Fecha { get; private set; }
    public DateTimeOffset? PublicadoEn { get; private set; }

    private MensajeOutbox()
    {
    }

    public static MensajeOutbox CrearPendiente(
        Guid eventId,
        Guid operadorId,
        Guid envioId,
        string tipo,
        string version,
        string payload,
        DateTimeOffset fecha)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("El EventId es requerido.", nameof(eventId));

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        if (envioId == Guid.Empty)
            throw new ArgumentException("El envío es requerido.", nameof(envioId));

        if (string.IsNullOrWhiteSpace(tipo))
            throw new ArgumentException("El tipo de mensaje es requerido.", nameof(tipo));

        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("La versión del contrato es requerida.", nameof(version));

        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("El payload es requerido.", nameof(payload));

        return new MensajeOutbox
        {
            EventId = eventId,
            OperadorId = operadorId,
            EnvioId = envioId,
            Tipo = tipo.Trim(),
            Version = version.Trim(),
            Payload = payload,
            Fecha = fecha
        };
    }

    public void MarcarPublicado(DateTimeOffset momento)
    {
        if (PublicadoEn is not null)
            throw new InvalidOperationException("El mensaje de outbox ya fue publicado.");

        PublicadoEn = momento;
    }
}
