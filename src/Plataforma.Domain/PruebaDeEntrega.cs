namespace Plataforma.Domain;

public class PruebaDeEntrega
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string Foto { get; private set; } = string.Empty;
    public string Firma { get; private set; } = string.Empty;
    public decimal? Latitud { get; private set; }
    public decimal? Longitud { get; private set; }
    public DateTime Fecha { get; private set; }

    private PruebaDeEntrega()
    {
    }

    public static PruebaDeEntrega Registrar(
        Guid id,
        Guid operadorId,
        Guid envioId,
        string foto,
        string firma,
        DateTime fecha,
        decimal? latitud = null,
        decimal? longitud = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La prueba de entrega necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La prueba de entrega necesita un operador.");

        if (envioId == Guid.Empty)
            throw new ArgumentException("La prueba de entrega necesita un envío.");

        return new PruebaDeEntrega
        {
            Id = id,
            OperadorId = operadorId,
            EnvioId = envioId,
            Foto = foto.Trim(),
            Firma = firma.Trim(),
            Latitud = latitud,
            Longitud = longitud,
            Fecha = fecha
        };
    }
}
