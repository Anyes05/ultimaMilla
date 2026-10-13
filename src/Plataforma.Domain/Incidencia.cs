namespace Plataforma.Domain;

public class Incidencia
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string Descripcion { get; private set; } = string.Empty;
    public DateTime Fecha { get; private set; }
    public bool Resuelta { get; private set; }

    private Incidencia()
    {
    }

    public static Incidencia Registrar(Guid id, Guid operadorId, Guid envioId, string descripcion, DateTime fecha)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La incidencia necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La incidencia necesita un operador.");

        if (envioId == Guid.Empty)
            throw new ArgumentException("La incidencia necesita un envío.");

        if (string.IsNullOrWhiteSpace(descripcion))
            throw new ArgumentException("La descripción es requerida.");

        return new Incidencia
        {
            Id = id,
            OperadorId = operadorId,
            EnvioId = envioId,
            Descripcion = descripcion.Trim(),
            Fecha = fecha,
            Resuelta = false
        };
    }
}
