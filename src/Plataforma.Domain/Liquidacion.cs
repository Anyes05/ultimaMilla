namespace Plataforma.Domain;

public class Liquidacion
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public DateTime PeriodoDesde { get; private set; }
    public DateTime PeriodoHasta { get; private set; }
    public decimal Monto { get; private set; }
    public bool Cerrada { get; private set; }

    private Liquidacion()
    {
    }

    public static Liquidacion Abrir(
        Guid id,
        Guid operadorId,
        Guid comercioId,
        DateTime periodoDesde,
        DateTime periodoHasta,
        decimal monto)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La liquidación necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La liquidación necesita un operador.");

        if (comercioId == Guid.Empty)
            throw new ArgumentException("La liquidación necesita un comercio.");

        if (periodoHasta < periodoDesde)
            throw new ArgumentException("El período hasta no puede ser anterior al desde.");

        if (monto < 0)
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto no puede ser negativo.");

        return new Liquidacion
        {
            Id = id,
            OperadorId = operadorId,
            ComercioId = comercioId,
            PeriodoDesde = periodoDesde,
            PeriodoHasta = periodoHasta,
            Monto = monto,
            Cerrada = false
        };
    }
}
