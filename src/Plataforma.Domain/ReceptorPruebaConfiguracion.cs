namespace Plataforma.Domain;

public class ReceptorPruebaConfiguracion
{
    public Guid OperadorId { get; private set; }
    public bool ForzarFallo { get; private set; }
    public DateTimeOffset ActualizadoEn { get; private set; }

    private ReceptorPruebaConfiguracion()
    {
    }

    public static ReceptorPruebaConfiguracion Crear(Guid operadorId, bool forzarFallo)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        return new ReceptorPruebaConfiguracion
        {
            OperadorId = operadorId,
            ForzarFallo = forzarFallo,
            ActualizadoEn = DateTimeOffset.UtcNow
        };
    }

    public void CambiarFallo(bool forzarFallo)
    {
        ForzarFallo = forzarFallo;
        ActualizadoEn = DateTimeOffset.UtcNow;
    }
}