namespace Plataforma.Domain;

public class ReglaTarifaria
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public decimal PrecioBase { get; private set; }
    public decimal PrecioPorKg { get; private set; }
    public decimal PrecioPorKm { get; private set; }
    public decimal RecargoFragil { get; private set; }
    public decimal RecargoUrgente { get; private set; }
    public bool Activa { get; private set; }

    private ReglaTarifaria()
    {
    }

    public static ReglaTarifaria Crear(
        Guid id,
        Guid operadorId,
        decimal precioBase,
        decimal precioPorKg,
        decimal precioPorKm,
        decimal recargoFragil,
        decimal recargoUrgente)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La regla tarifaria necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La regla tarifaria necesita un operador.");

        if (precioBase < 0 || precioPorKg < 0 || precioPorKm < 0 || recargoFragil < 0 || recargoUrgente < 0)
            throw new ArgumentOutOfRangeException(nameof(precioBase), "Los importes de la tarifa no pueden ser negativos.");

        return new ReglaTarifaria
        {
            Id = id,
            OperadorId = operadorId,
            PrecioBase = precioBase,
            PrecioPorKg = precioPorKg,
            PrecioPorKm = precioPorKm,
            RecargoFragil = recargoFragil,
            RecargoUrgente = recargoUrgente,
            Activa = true
        };
    }
}
