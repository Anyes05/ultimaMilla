namespace Plataforma.Domain;

public class VigenciaTarifa
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ReglaTarifariaId { get; private set; }
    public DateTime VigenciaDesde { get; private set; }
    public DateTime VigenciaHasta { get; private set; }

    private VigenciaTarifa()
    {
    }

    public static VigenciaTarifa Crear(
        Guid id,
        Guid operadorId,
        Guid reglaTarifariaId,
        DateTime vigenciaDesde,
        DateTime vigenciaHasta)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La vigencia necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La vigencia necesita un operador.");

        if (reglaTarifariaId == Guid.Empty)
            throw new ArgumentException("La vigencia necesita una regla tarifaria.");

        if (vigenciaHasta < vigenciaDesde)
            throw new ArgumentException("La vigencia hasta no puede ser anterior al desde.");

        return new VigenciaTarifa
        {
            Id = id,
            OperadorId = operadorId,
            ReglaTarifariaId = reglaTarifariaId,
            VigenciaDesde = vigenciaDesde,
            VigenciaHasta = vigenciaHasta
        };
    }

    /// <summary>Cierra la vigencia abierta (sin superponer con la siguiente).</summary>
    public void Cerrar(DateTime vigenciaHasta)
    {
        if (vigenciaHasta < VigenciaDesde)
            throw new ArgumentException("La vigencia hasta no puede ser anterior al desde.");

        VigenciaHasta = vigenciaHasta.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(vigenciaHasta, DateTimeKind.Utc)
            : vigenciaHasta.ToUniversalTime();
    }
}
