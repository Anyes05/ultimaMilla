namespace Plataforma.Domain;

public class Ruta
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid RepartidorId { get; private set; }
    public DateOnly Fecha { get; private set; }

    private Ruta()
    {
    }

    public static Ruta Programar(Guid operadorId, Guid repartidorId, DateOnly fecha)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("La ruta necesita un operador.");

        if (repartidorId == Guid.Empty)
            throw new ArgumentException("La ruta necesita un repartidor.");

        return new Ruta
        {
            Id = Guid.NewGuid(),
            OperadorId = operadorId,
            RepartidorId = repartidorId,
            Fecha = fecha
        };
    }
}
