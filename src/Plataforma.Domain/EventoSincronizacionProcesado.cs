namespace Plataforma.Domain;

/// <summary>
/// Idempotencia durable de la sync del repartidor: un <see cref="EventoId"/>
/// ya aplicado no se vuelve a ejecutar (reintento tras OK perdido → mismo resultado).
/// </summary>
public class EventoSincronizacionProcesado
{
    public Guid EventoId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public EstadoEnvio Estado { get; private set; }
    public int IntentosRealizados { get; private set; }
    public DateTimeOffset ProcesadoEn { get; private set; }

    private EventoSincronizacionProcesado()
    {
    }

    public static EventoSincronizacionProcesado Registrar(
        Guid eventoId,
        Guid operadorId,
        Guid envioId,
        EstadoEnvio estado,
        int intentosRealizados,
        DateTimeOffset? procesadoEn = null)
    {
        if (eventoId == Guid.Empty)
            throw new ArgumentException("El eventoId es requerido.", nameof(eventoId));

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        if (envioId == Guid.Empty)
            throw new ArgumentException("El envío es requerido.", nameof(envioId));

        if (intentosRealizados < 0)
            throw new ArgumentOutOfRangeException(nameof(intentosRealizados));

        return new EventoSincronizacionProcesado
        {
            EventoId = eventoId,
            OperadorId = operadorId,
            EnvioId = envioId,
            Estado = estado,
            IntentosRealizados = intentosRealizados,
            ProcesadoEn = procesadoEn ?? DateTimeOffset.UtcNow
        };
    }
}
