namespace Plataforma.Domain;

/// <summary>Ventana horaria de cobertura / entrega (RF 2).</summary>
public class FranjaHoraria
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid? ZonaId { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public TimeOnly Desde { get; private set; }
    public TimeOnly Hasta { get; private set; }
    public bool Activa { get; private set; }

    private FranjaHoraria()
    {
    }

    public static FranjaHoraria Definir(
        Guid id,
        Guid operadorId,
        string nombre,
        TimeOnly desde,
        TimeOnly hasta,
        Guid? zonaId = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La franja necesita un identificador.", nameof(id));
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre es requerido.", nameof(nombre));
        if (hasta <= desde)
            throw new ArgumentException("La hora hasta debe ser posterior a desde.");

        return new FranjaHoraria
        {
            Id = id,
            OperadorId = operadorId,
            ZonaId = zonaId,
            Nombre = nombre.Trim(),
            Desde = desde,
            Hasta = hasta,
            Activa = true
        };
    }

    public void Actualizar(string nombre, TimeOnly desde, TimeOnly hasta, Guid? zonaId = null)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre es requerido.", nameof(nombre));
        if (hasta <= desde)
            throw new ArgumentException("La hora hasta debe ser posterior a desde.");
        Nombre = nombre.Trim();
        Desde = desde;
        Hasta = hasta;
        ZonaId = zonaId;
    }

    public void Desactivar() => Activa = false;

    public void Activar() => Activa = true;
}
