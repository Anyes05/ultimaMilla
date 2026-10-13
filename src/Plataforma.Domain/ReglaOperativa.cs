namespace Plataforma.Domain;

public class ReglaOperativa
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public int MaximoIntentos { get; private set; }
    public int PlazoEntregaHoras { get; private set; }
    public bool ExigeFirma { get; private set; }
    public bool ExigeFoto { get; private set; }
    public bool ExigeGeocerca { get; private set; }
    public int PlazoReprogramacionHoras { get; private set; }
    public DateTime VigenciaDesde { get; private set; }
    public DateTime VigenciaHasta { get; private set; }
    public bool Activa { get; private set; }

    private ReglaOperativa()
    {
    }

    public static ReglaOperativa Crear(
        Guid id,
        Guid operadorId,
        int maximoIntentos,
        int plazoEntregaHoras,
        bool exigeFirma,
        bool exigeFoto,
        bool exigeGeocerca,
        int plazoReprogramacionHoras,
        DateTime vigenciaDesde,
        DateTime vigenciaHasta)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La regla operativa necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La regla operativa necesita un operador.");

        if (maximoIntentos < 1)
            throw new ArgumentOutOfRangeException(nameof(maximoIntentos), "El máximo de intentos debe ser al menos 1.");

        if (vigenciaHasta < vigenciaDesde)
            throw new ArgumentException("La vigencia hasta no puede ser anterior al desde.");

        return new ReglaOperativa
        {
            Id = id,
            OperadorId = operadorId,
            MaximoIntentos = maximoIntentos,
            PlazoEntregaHoras = plazoEntregaHoras,
            ExigeFirma = exigeFirma,
            ExigeFoto = exigeFoto,
            ExigeGeocerca = exigeGeocerca,
            PlazoReprogramacionHoras = plazoReprogramacionHoras,
            VigenciaDesde = vigenciaDesde,
            VigenciaHasta = vigenciaHasta,
            Activa = true
        };
    }
}
