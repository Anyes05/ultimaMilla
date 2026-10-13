namespace Plataforma.Domain;

public class Direccion
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string Calle { get; private set; } = string.Empty;
    public string Numero { get; private set; } = string.Empty;
    public string Apartamento { get; private set; } = string.Empty;
    public string Referencia { get; private set; } = string.Empty;
    public decimal? Latitud { get; private set; }
    public decimal? Longitud { get; private set; }

    private Direccion()
    {
    }

    public static Direccion Registrar(
        Guid id,
        Guid operadorId,
        Guid envioId,
        string calle,
        string numero,
        string apartamento = "",
        string referencia = "",
        decimal? latitud = null,
        decimal? longitud = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La dirección necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La dirección necesita un operador.");

        if (envioId == Guid.Empty)
            throw new ArgumentException("La dirección necesita un envío.");

        if (string.IsNullOrWhiteSpace(calle))
            throw new ArgumentException("La calle es requerida.");

        return new Direccion
        {
            Id = id,
            OperadorId = operadorId,
            EnvioId = envioId,
            Calle = calle.Trim(),
            Numero = numero.Trim(),
            Apartamento = apartamento.Trim(),
            Referencia = referencia.Trim(),
            Latitud = latitud,
            Longitud = longitud
        };
    }
}
