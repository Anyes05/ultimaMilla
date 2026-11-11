namespace Plataforma.Domain;

public enum EstadoRetiro
{
    Solicitado,
    Programado,
    EnCurso,
    Completado,
    Cancelado
}

/// <summary>Retiro en domicilio solicitado por el comercio (RF 37–41).</summary>
public class SolicitudRetiro
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Direccion { get; private set; } = string.Empty;
    public string Contacto { get; private set; } = string.Empty;
    public string Observaciones { get; private set; } = string.Empty;
    public DateOnly FechaSolicitada { get; private set; }
    public EstadoRetiro Estado { get; private set; }
    public Guid? RutaId { get; private set; }
    public Guid? EnvioAsociadoId { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }

    /// <summary>Tarifa del retiro al pedirlo (RF 41), con la versión tarifaria que la fijó.</summary>
    public decimal TarifaCalculada { get; private set; }
    public Guid? ReglaTarifariaId { get; private set; }

    private SolicitudRetiro()
    {
    }

    public static SolicitudRetiro Solicitar(
        Guid id,
        Guid operadorId,
        Guid comercioId,
        string direccion,
        string contacto,
        DateOnly fechaSolicitada,
        string observaciones = "",
        decimal tarifa = 0,
        Guid? reglaTarifariaId = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El retiro necesita un identificador.", nameof(id));
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));
        if (comercioId == Guid.Empty)
            throw new ArgumentException("El comercio es requerido.", nameof(comercioId));
        if (string.IsNullOrWhiteSpace(direccion))
            throw new ArgumentException("La dirección es requerida.", nameof(direccion));
        if (tarifa < 0)
            throw new ArgumentOutOfRangeException(nameof(tarifa), "La tarifa del retiro no puede ser negativa.");

        return new SolicitudRetiro
        {
            Id = id,
            OperadorId = operadorId,
            ComercioId = comercioId,
            Direccion = direccion.Trim(),
            Contacto = (contacto ?? string.Empty).Trim(),
            Observaciones = (observaciones ?? string.Empty).Trim(),
            FechaSolicitada = fechaSolicitada,
            Estado = EstadoRetiro.Solicitado,
            CreadoEn = DateTimeOffset.UtcNow,
            TarifaCalculada = tarifa,
            ReglaTarifariaId = reglaTarifariaId
        };
    }

    public void ProgramarEnRuta(Guid rutaId)
    {
        if (rutaId == Guid.Empty)
            throw new ArgumentException("La ruta es requerida.", nameof(rutaId));
        if (Estado is not (EstadoRetiro.Solicitado or EstadoRetiro.Programado))
            throw new InvalidOperationException($"No se puede programar un retiro en estado {Estado}.");
        RutaId = rutaId;
        Estado = EstadoRetiro.Programado;
    }

    public void MarcarEnCurso() => Estado = EstadoRetiro.EnCurso;

    public void Completar(Guid? envioAsociadoId = null)
    {
        if (Estado is EstadoRetiro.Cancelado or EstadoRetiro.Completado)
            throw new InvalidOperationException($"El retiro ya está {Estado}.");
        Estado = EstadoRetiro.Completado;
        EnvioAsociadoId = envioAsociadoId;
    }

    public void Cancelar(string motivo)
    {
        if (Estado == EstadoRetiro.Completado)
            throw new InvalidOperationException("No se puede cancelar un retiro completado.");
        Estado = EstadoRetiro.Cancelado;
        if (!string.IsNullOrWhiteSpace(motivo))
            Observaciones = string.IsNullOrWhiteSpace(Observaciones)
                ? motivo.Trim()
                : $"{Observaciones} | Cancelado: {motivo.Trim()}";
    }
}
