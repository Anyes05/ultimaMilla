namespace Plataforma.Domain;

public class HistorialEstado
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public EstadoEnvio Estado { get; private set; }
    public DateTime Fecha { get; private set; }
    public string Motivo { get; private set; } = string.Empty;

    private HistorialEstado()
    {
    }

    public static HistorialEstado Registrar(
        Guid id,
        Guid operadorId,
        Guid envioId,
        EstadoEnvio estado,
        DateTime fecha,
        string motivo = "")
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El historial necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El historial necesita un operador.");

        if (envioId == Guid.Empty)
            throw new ArgumentException("El historial necesita un envío.");

        return new HistorialEstado
        {
            Id = id,
            OperadorId = operadorId,
            EnvioId = envioId,
            Estado = estado,
            Fecha = fecha,
            Motivo = motivo.Trim()
        };
    }
}
