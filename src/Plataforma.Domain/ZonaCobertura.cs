namespace Plataforma.Domain;

public class ZonaCobertura
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public string Poligono { get; private set; } = string.Empty;

    private ZonaCobertura()
    {
    }

    public static ZonaCobertura Definir(Guid id, Guid operadorId, string nombre, string poligono)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La zona necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La zona necesita un operador.");

        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre de la zona es requerido.");

        return new ZonaCobertura
        {
            Id = id,
            OperadorId = operadorId,
            Nombre = nombre.Trim(),
            Poligono = poligono.Trim()
        };
    }
}
