namespace Plataforma.Domain;

public class Comercio
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public string NombreComercial { get; private set; } = string.Empty;
    public string RazonSocial { get; private set; } = string.Empty;
    public string DocumentoFiscal { get; private set; } = string.Empty;
    public string DireccionPrincipal { get; private set; } = string.Empty;
    public bool Activo { get; private set; }

    private Comercio()
    {
    }

    public static Comercio Registrar(
        Guid id,
        Guid operadorId,
        string nombreComercial,
        string razonSocial,
        string documentoFiscal,
        string direccionPrincipal)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El comercio necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El comercio necesita un operador.");

        if (string.IsNullOrWhiteSpace(nombreComercial))
            throw new ArgumentException("El nombre comercial es requerido.");

        return new Comercio
        {
            Id = id,
            OperadorId = operadorId,
            NombreComercial = nombreComercial.Trim(),
            RazonSocial = razonSocial.Trim(),
            DocumentoFiscal = documentoFiscal.Trim(),
            DireccionPrincipal = direccionPrincipal.Trim(),
            Activo = true
        };
    }
}
