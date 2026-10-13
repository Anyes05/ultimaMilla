namespace Plataforma.Domain;

public class Vehiculo
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public string Patente { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public bool Activo { get; private set; }

    private Vehiculo()
    {
    }

    public static Vehiculo Registrar(Guid id, Guid operadorId, string patente, string descripcion)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El vehículo necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El vehículo necesita un operador.");

        if (string.IsNullOrWhiteSpace(patente))
            throw new ArgumentException("La patente es requerida.");

        return new Vehiculo
        {
            Id = id,
            OperadorId = operadorId,
            Patente = patente.Trim(),
            Descripcion = descripcion.Trim(),
            Activo = true
        };
    }
}
