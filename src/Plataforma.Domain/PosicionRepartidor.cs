namespace Plataforma.Domain;

/// <summary>Posición del repartidor reportada por la app durante la jornada (RF 23).</summary>
public class PosicionRepartidor
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid RepartidorId { get; private set; }
    public decimal Latitud { get; private set; }
    public decimal Longitud { get; private set; }
    public decimal? PrecisionMetros { get; private set; }

    /// <summary>Hora del teléfono al tomar la posición, en UTC (PostgreSQL guarda timestamptz en UTC).</summary>
    public DateTime FechaUtc { get; private set; }

    /// <summary>Hora en que llegó al servidor: con la app sin señal, puede ser bastante después.</summary>
    public DateTime RecibidaUtc { get; private set; }

    private PosicionRepartidor()
    {
    }

    public static PosicionRepartidor Registrar(
        Guid id,
        Guid operadorId,
        Guid repartidorId,
        decimal latitud,
        decimal longitud,
        DateTimeOffset fechaDispositivo,
        DateTime recibidaUtc,
        decimal? precisionMetros = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La posición necesita un identificador.", nameof(id));
        if (operadorId == Guid.Empty)
            throw new ArgumentException("La posición necesita un operador.", nameof(operadorId));
        if (repartidorId == Guid.Empty)
            throw new ArgumentException("La posición necesita un repartidor.", nameof(repartidorId));
        if (latitud is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitud), "La latitud debe estar entre -90 y 90.");
        if (longitud is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitud), "La longitud debe estar entre -180 y 180.");
        if (precisionMetros is < 0)
            throw new ArgumentOutOfRangeException(nameof(precisionMetros), "La precisión no puede ser negativa.");

        return new PosicionRepartidor
        {
            Id = id,
            OperadorId = operadorId,
            RepartidorId = repartidorId,
            Latitud = latitud,
            Longitud = longitud,
            PrecisionMetros = precisionMetros,
            FechaUtc = fechaDispositivo.UtcDateTime,
            RecibidaUtc = DateTime.SpecifyKind(recibidaUtc, DateTimeKind.Utc)
        };
    }
}
