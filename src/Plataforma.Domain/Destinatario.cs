namespace Plataforma.Domain;

public class Destinatario
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string NombreCompleto { get; private set; } = string.Empty;
    public string Documento { get; private set; } = string.Empty;
    public string Telefono { get; private set; } = string.Empty;

    private Destinatario()
    {
    }

    public static Destinatario Registrar(
        Guid id,
        Guid operadorId,
        Guid envioId,
        string nombreCompleto,
        string documento,
        string telefono)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El destinatario necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El destinatario necesita un operador.");

        if (envioId == Guid.Empty)
            throw new ArgumentException("El destinatario necesita un envío.");

        if (string.IsNullOrWhiteSpace(nombreCompleto))
            throw new ArgumentException("El nombre del destinatario es requerido.");

        return new Destinatario
        {
            Id = id,
            OperadorId = operadorId,
            EnvioId = envioId,
            NombreCompleto = nombreCompleto.Trim(),
            Documento = documento.Trim(),
            Telefono = telefono.Trim()
        };
    }
}
