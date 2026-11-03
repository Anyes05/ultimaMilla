namespace Plataforma.Domain;

/// <summary>Unidad física dentro de un envío (RF 8 / cambio 31–36).</summary>
public class Bulto
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string Codigo { get; private set; } = string.Empty;
    public decimal PesoKg { get; private set; }
    public decimal AltoCm { get; private set; }
    public decimal AnchoCm { get; private set; }
    public decimal LargoCm { get; private set; }
    public EstadoBulto Estado { get; private set; }
    public string? MotivoNoEntrega { get; private set; }

    private Bulto()
    {
    }

    public static Bulto Crear(
        Guid id,
        Guid operadorId,
        Guid envioId,
        string codigo,
        decimal pesoKg = 0,
        decimal altoCm = 0,
        decimal anchoCm = 0,
        decimal largoCm = 0)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El bulto necesita un identificador.", nameof(id));
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El bulto necesita un operador.", nameof(operadorId));
        if (envioId == Guid.Empty)
            throw new ArgumentException("El bulto necesita un envío.", nameof(envioId));
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("El código del bulto es requerido.", nameof(codigo));
        if (pesoKg <= 0)
            throw new ArgumentOutOfRangeException(nameof(pesoKg), "El peso del bulto debe ser mayor que 0.");
        if (altoCm < 0 || anchoCm < 0 || largoCm < 0)
            throw new ArgumentOutOfRangeException(nameof(altoCm), "Las dimensiones no pueden ser negativas.");

        return new Bulto
        {
            Id = id,
            OperadorId = operadorId,
            EnvioId = envioId,
            Codigo = codigo.Trim(),
            PesoKg = pesoKg,
            AltoCm = altoCm,
            AnchoCm = anchoCm,
            LargoCm = largoCm,
            Estado = EstadoBulto.Pendiente
        };
    }

    public void CambiarEstado(EstadoBulto nuevoEstado, string? motivoNoEntrega = null)
    {
        if (!TransicionesBulto.EsValida(Estado, nuevoEstado))
            throw new InvalidOperationException($"Transición de bulto inválida: {Estado} -> {nuevoEstado}");

        if ((nuevoEstado is EstadoBulto.NoEntregado or EstadoBulto.PendienteNuevaVisita) &&
            string.IsNullOrWhiteSpace(motivoNoEntrega) && string.IsNullOrWhiteSpace(MotivoNoEntrega))
            throw new InvalidOperationException("El motivo de no entrega es requerido para este estado.");

        Estado = nuevoEstado;
        if (!string.IsNullOrWhiteSpace(motivoNoEntrega))
            MotivoNoEntrega = motivoNoEntrega.Trim();
        if (nuevoEstado == EstadoBulto.Entregado)
            MotivoNoEntrega = null;
    }

    public bool DisponibleParaNuevaRuta => DerivacionEstadoEnvio.DisponibleParaNuevaRuta(Estado);
}
