namespace Plataforma.Domain;

public class Novedad
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string Descripcion { get; private set; } = string.Empty;
    public DateTime Fecha { get; private set; }

    private Novedad()
    {
    }

    public static Novedad Registrar(Guid id, Guid operadorId, Guid envioId, string descripcion, DateTime fecha)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La novedad necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La novedad necesita un operador.");

        if (envioId == Guid.Empty)
            throw new ArgumentException("La novedad necesita un envío.");

        if (string.IsNullOrWhiteSpace(descripcion))
            throw new ArgumentException("La descripción es requerida.");

        return new Novedad
        {
            Id = id,
            OperadorId = operadorId,
            EnvioId = envioId,
            Descripcion = descripcion.Trim(),
            Fecha = fecha
        };
    }
}
