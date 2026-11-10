namespace Plataforma.Domain;

public class Vehiculo
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public string Patente { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public bool Activo { get; private set; }
    /// <summary>Capacidad máxima de peso en kg (RF 6 / validación de despacho).</summary>
    public decimal CapacidadPesoKg { get; private set; }
    /// <summary>Capacidad máxima de volumen en cm³ (RF 6).</summary>
    public decimal CapacidadVolumenCm3 { get; private set; }
    /// <summary>Máximo de paradas recomendadas por ruta.</summary>
    public int MaximoParadas { get; private set; }

    private Vehiculo()
    {
    }

    public static Vehiculo Registrar(
        Guid id,
        Guid operadorId,
        string patente,
        string descripcion,
        decimal capacidadPesoKg = 0,
        decimal capacidadVolumenCm3 = 0,
        int maximoParadas = 0)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El vehículo necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El vehículo necesita un operador.");

        if (string.IsNullOrWhiteSpace(patente))
            throw new ArgumentException("La patente es requerida.");

        if (capacidadPesoKg < 0 || capacidadVolumenCm3 < 0 || maximoParadas < 0)
            throw new ArgumentOutOfRangeException(nameof(capacidadPesoKg), "Las capacidades no pueden ser negativas.");

        return new Vehiculo
        {
            Id = id,
            OperadorId = operadorId,
            Patente = patente.Trim(),
            Descripcion = descripcion.Trim(),
            Activo = true,
            CapacidadPesoKg = capacidadPesoKg,
            CapacidadVolumenCm3 = capacidadVolumenCm3,
            MaximoParadas = maximoParadas
        };
    }

    public void ActualizarCapacidad(decimal pesoKg, decimal volumenCm3, int maximoParadas)
    {
        if (pesoKg < 0 || volumenCm3 < 0 || maximoParadas < 0)
            throw new ArgumentOutOfRangeException(nameof(pesoKg));
        CapacidadPesoKg = pesoKg;
        CapacidadVolumenCm3 = volumenCm3;
        MaximoParadas = maximoParadas;
    }

    public void Desactivar() => Activo = false;
}
