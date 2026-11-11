namespace Plataforma.Domain;

public class Ruta
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid RepartidorId { get; private set; }
    public DateOnly Fecha { get; private set; }

    /// <summary>Vehículo elegido al despachar; contra su capacidad se validan los retiros que se suman (RF 39).</summary>
    public Guid? VehiculoId { get; private set; }

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

    public void AsignarVehiculo(Guid vehiculoId)
    {
        if (vehiculoId == Guid.Empty)
            throw new ArgumentException("El vehículo es requerido.", nameof(vehiculoId));
        VehiculoId = vehiculoId;
    }
}
