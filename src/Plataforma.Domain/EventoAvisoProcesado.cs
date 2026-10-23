namespace Plataforma.Domain;

public class EventoAvisoProcesado
{
    public Guid EventId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public DateTimeOffset ProcesadoEn { get; private set; }

    private EventoAvisoProcesado()
    {
    }

    public static EventoAvisoProcesado Registrar(Guid eventId, Guid operadorId, Guid envioId)
    {
        if (eventId == Guid.Empty || operadorId == Guid.Empty || envioId == Guid.Empty)
            throw new ArgumentException("El evento, operador y envío son requeridos.");

        return new EventoAvisoProcesado
        {
            EventId = eventId,
            OperadorId = operadorId,
            EnvioId = envioId,
            ProcesadoEn = DateTimeOffset.UtcNow
        };
    }
}