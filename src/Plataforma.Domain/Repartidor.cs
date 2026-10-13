namespace Plataforma.Domain;

public class Repartidor
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public string Documento { get; private set; } = string.Empty;
    public string Telefono { get; private set; } = string.Empty;
    public bool Activo { get; private set; }

    private Repartidor()
    {
    }

    public static Repartidor Registrar(
        Guid id,
        Guid operadorId,
        string nombre,
        string documento,
        string telefono,
        Guid? usuarioId = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El repartidor necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El repartidor necesita un operador.");

        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre del repartidor es requerido.");

        return new Repartidor
        {
            Id = id,
            OperadorId = operadorId,
            UsuarioId = usuarioId,
            Nombre = nombre.Trim(),
            Documento = documento.Trim(),
            Telefono = telefono.Trim(),
            Activo = true
        };
    }
}
