namespace Plataforma.Domain;

public class AvisoComercio
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Mensaje { get; private set; } = string.Empty;
    public DateTime Fecha { get; private set; }
    public bool Leido { get; private set; }

    private AvisoComercio()
    {
    }

    public static AvisoComercio Crear(Guid id, Guid operadorId, Guid comercioId, string mensaje, DateTime fecha)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El aviso necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El aviso necesita un operador.");

        if (comercioId == Guid.Empty)
            throw new ArgumentException("El aviso necesita un comercio.");

        if (string.IsNullOrWhiteSpace(mensaje))
            throw new ArgumentException("El mensaje es requerido.");

        return new AvisoComercio
        {
            Id = id,
            OperadorId = operadorId,
            ComercioId = comercioId,
            Mensaje = mensaje.Trim(),
            Fecha = fecha,
            Leido = false
        };
    }
}
