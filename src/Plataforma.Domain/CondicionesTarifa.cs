namespace Plataforma.Domain;

/// <summary>Recargo propio de una zona en una versión de la tarifa (RF 3: variación por zona).</summary>
public class RecargoZonaTarifa
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ReglaTarifariaId { get; private set; }
    public Guid ZonaId { get; private set; }
    public decimal Recargo { get; private set; }

    private RecargoZonaTarifa()
    {
    }

    public static RecargoZonaTarifa Crear(Guid id, Guid operadorId, Guid reglaTarifariaId, Guid zonaId, decimal recargo)
    {
        if (id == Guid.Empty || operadorId == Guid.Empty || reglaTarifariaId == Guid.Empty || zonaId == Guid.Empty)
            throw new ArgumentException("El recargo por zona necesita su regla, su zona y su operador.");
        if (recargo is < 0 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(recargo), "El recargo de una zona debe estar entre 0 y 1.000.000.");

        return new RecargoZonaTarifa
        {
            Id = id,
            OperadorId = operadorId,
            ReglaTarifariaId = reglaTarifariaId,
            ZonaId = zonaId,
            Recargo = recargo
        };
    }
}

/// <summary>
/// Bonificación de una versión de la tarifa (RF 3): un porcentaje de descuento para un comercio, a partir de
/// cierta cantidad de envíos del comercio en el mes, o las dos condiciones juntas.
/// </summary>
public class BonificacionTarifa
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ReglaTarifariaId { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public decimal Porcentaje { get; private set; }
    public Guid? ComercioId { get; private set; }
    public int? MinimoEnviosMes { get; private set; }

    private BonificacionTarifa()
    {
    }

    public static BonificacionTarifa Crear(
        Guid id,
        Guid operadorId,
        Guid reglaTarifariaId,
        string nombre,
        decimal porcentaje,
        Guid? comercioId = null,
        int? minimoEnviosMes = null)
    {
        if (id == Guid.Empty || operadorId == Guid.Empty || reglaTarifariaId == Guid.Empty)
            throw new ArgumentException("La bonificación necesita su regla y su operador.");
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 80)
            throw new ArgumentException("La bonificación necesita un nombre de hasta 80 caracteres.", nameof(nombre));
        if (porcentaje is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(porcentaje), "El porcentaje de una bonificación debe ser mayor que 0 y de hasta 100.");
        if (minimoEnviosMes is < 1)
            throw new ArgumentOutOfRangeException(nameof(minimoEnviosMes), "El mínimo de envíos en el mes debe ser al menos 1.");
        if (comercioId == Guid.Empty)
            throw new ArgumentException("El comercio de la bonificación no es válido.", nameof(comercioId));

        return new BonificacionTarifa
        {
            Id = id,
            OperadorId = operadorId,
            ReglaTarifariaId = reglaTarifariaId,
            Nombre = nombre.Trim(),
            Porcentaje = porcentaje,
            ComercioId = comercioId,
            MinimoEnviosMes = minimoEnviosMes
        };
    }

    /// <summary>
    /// Aplica si el comercio coincide (o la bonificación es para todos) y, con mínimo, si este envío es el número
    /// mínimo del mes o posterior.
    /// </summary>
    public bool AplicaA(Guid? comercioId, int numeroDeEnvioEnElMes) =>
        (ComercioId is null || ComercioId == comercioId)
        && (MinimoEnviosMes is null || numeroDeEnvioEnElMes >= MinimoEnviosMes);
}
