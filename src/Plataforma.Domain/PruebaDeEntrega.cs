namespace Plataforma.Domain;

public class PruebaDeEntrega
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string Foto { get; private set; } = string.Empty;
    public string Firma { get; private set; } = string.Empty;
    public string? CodigoEscaneado { get; private set; }
    public decimal? Latitud { get; private set; }
    public decimal? Longitud { get; private set; }
    public DateTime Fecha { get; private set; }
    public string NombreReceptor { get; private set; } = string.Empty;
    public string Observaciones { get; private set; } = string.Empty;
    public string Usuario { get; private set; } = string.Empty;
    public DateTimeOffset FechaDispositivo { get; private set; }

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
            Fecha = fecha,
            FechaDispositivo = new DateTimeOffset(DateTime.SpecifyKind(fecha, DateTimeKind.Utc))
        };
    }

    /// <summary>Comprobante offline del móvil (receptor, observaciones, hora del dispositivo y usuario).</summary>
    public static PruebaDeEntrega DesdeSincronizacion(
        Guid id,
        Guid operadorId,
        Guid envioId,
        string nombreReceptor,
        string observaciones,
        string usuario,
        DateTimeOffset fechaDispositivo,
        string? foto = null,
        string? firma = null,
        decimal? latitud = null,
        decimal? longitud = null,
        string? codigoEscaneado = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La prueba de entrega necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La prueba de entrega necesita un operador.");

        if (envioId == Guid.Empty)
            throw new ArgumentException("La prueba de entrega necesita un envío.");

        if (string.IsNullOrWhiteSpace(usuario))
            throw new ArgumentException("El usuario del comprobante es requerido.");

        return new PruebaDeEntrega
        {
            Id = id,
            OperadorId = operadorId,
            EnvioId = envioId,
            NombreReceptor = (nombreReceptor ?? string.Empty).Trim(),
            Observaciones = (observaciones ?? string.Empty).Trim(),
            Usuario = usuario.Trim(),
            Foto = foto ?? string.Empty,
            Firma = firma ?? string.Empty,
            Latitud = latitud,
            Longitud = longitud,
            CodigoEscaneado = codigoEscaneado,
            FechaDispositivo = fechaDispositivo,
            Fecha = fechaDispositivo.UtcDateTime
        };
    }
}
