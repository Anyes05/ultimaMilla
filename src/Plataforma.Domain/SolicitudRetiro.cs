namespace Plataforma.Domain;

/// <summary>
/// Estados del retiro (RF 38). Los valores no cambian: el worker los usa en SQL y ya hay datos guardados.
/// Completado es "Retirado" (el repartidor ya tiene los paquetes).
/// </summary>
public enum EstadoRetiro
{
    Solicitado,
    Programado,
    EnCurso,
    Completado,
    Cancelado,
    NoRetirado,
    EnDeposito
}

/// <summary>Transiciones permitidas del retiro, declaradas en un solo lugar (convive con la máquina del envío).</summary>
public static class TransicionesRetiro
{
    private static readonly IReadOnlyDictionary<EstadoRetiro, EstadoRetiro[]> Permitidas = new Dictionary<EstadoRetiro, EstadoRetiro[]>
    {
        [EstadoRetiro.Solicitado] = [EstadoRetiro.Programado, EstadoRetiro.Completado, EstadoRetiro.Cancelado],
        [EstadoRetiro.Programado] = [EstadoRetiro.Programado, EstadoRetiro.EnCurso, EstadoRetiro.Completado, EstadoRetiro.NoRetirado, EstadoRetiro.Cancelado],
        [EstadoRetiro.EnCurso] = [EstadoRetiro.Completado, EstadoRetiro.NoRetirado, EstadoRetiro.Cancelado],
        [EstadoRetiro.NoRetirado] = [EstadoRetiro.Programado, EstadoRetiro.Cancelado],
        [EstadoRetiro.Completado] = [EstadoRetiro.EnDeposito],
        [EstadoRetiro.EnDeposito] = [],
        [EstadoRetiro.Cancelado] = []
    };

    public static bool EsValida(EstadoRetiro desde, EstadoRetiro hacia) =>
        Permitidas.TryGetValue(desde, out var destinos) && destinos.Contains(hacia);
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

    /// <summary>Carga estimada que declara el comercio (RF 37); ocupa lugar en el vehículo desde su parada (RF 39).</summary>
    public int CantidadBultos { get; private set; } = 1;
    public decimal PesoKg { get; private set; }
    public decimal VolumenCm3 { get; private set; }

    /// <summary>Parada de la ruta después de la cual se retira (0 = antes de la primera entrega; vacío = al final).</summary>
    public int? PosicionEnRuta { get; private set; }

    /// <summary>Motivo del último intento sin retirar (RF 38).</summary>
    public string MotivoNoRetiro { get; private set; } = string.Empty;

    /// <summary>Franja horaria en la que el comprador espera el retiro (RF 37); vacía = cualquier horario.</summary>
    public Guid? FranjaId { get; private set; }

    /// <summary>Por qué se pide el retiro (RF 37): cambio, devolución, producto fallado u otro.</summary>
    public string Motivo { get; private set; } = string.Empty;

    /// <summary>Motivos que ofrece el portal; por API se acepta cualquier texto.</summary>
    public static readonly IReadOnlyList<string> MotivosSugeridos =
        ["Cambio de producto", "Devolución del comprador", "Producto fallado", "Otro"];

    /// <summary>Los paquetes ya están en manos del operador (en el vehículo o en el depósito).</summary>
    public bool FueRetirado => Estado is EstadoRetiro.Completado or EstadoRetiro.EnDeposito;

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
        Guid? reglaTarifariaId = null,
        int cantidadBultos = 1,
        decimal pesoKg = 0,
        decimal volumenCm3 = 0,
        Guid? franjaId = null,
        string motivo = "")
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
        if (cantidadBultos is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(cantidadBultos), "La cantidad de bultos del retiro debe estar entre 1 y 100.");
        if (pesoKg < 0 || volumenCm3 < 0)
            throw new ArgumentOutOfRangeException(nameof(pesoKg), "El peso y el volumen del retiro no pueden ser negativos.");
        if ((motivo ?? string.Empty).Trim().Length > 200)
            throw new ArgumentException("El motivo del retiro es demasiado largo.", nameof(motivo));
        if (franjaId == Guid.Empty)
            franjaId = null;

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
            ReglaTarifariaId = reglaTarifariaId,
            CantidadBultos = cantidadBultos,
            PesoKg = pesoKg,
            VolumenCm3 = volumenCm3,
            FranjaId = franjaId,
            Motivo = (motivo ?? string.Empty).Trim()
        };
    }

    /// <summary>
    /// Lo asigna a una ruta. La ruta tiene que ser del día pedido; solo un retiro no retirado se puede pasar a
    /// una ruta de otro día (nueva visita), y entonces la fecha del retiro pasa a ser la de esa ruta.
    /// </summary>
    public void ProgramarEnRuta(Guid rutaId, int? posicionEnRuta = null, DateOnly? fechaRuta = null)
    {
        if (rutaId == Guid.Empty)
            throw new ArgumentException("La ruta es requerida.", nameof(rutaId));
        if (posicionEnRuta < 0)
            throw new ArgumentOutOfRangeException(nameof(posicionEnRuta), "La parada no puede ser negativa.");
        if (fechaRuta is DateOnly fecha && fecha != FechaSolicitada && Estado != EstadoRetiro.NoRetirado)
            throw new InvalidOperationException("Elegí una ruta del día del retiro.");
        Pasar(EstadoRetiro.Programado, $"No se puede programar un retiro en estado {Estado}.");
        RutaId = rutaId;
        PosicionEnRuta = posicionEnRuta;
        if (fechaRuta is DateOnly nueva)
            FechaSolicitada = nueva;
    }

    /// <summary>Salió la ruta que lo lleva.</summary>
    public void MarcarEnCurso() =>
        Pasar(EstadoRetiro.EnCurso, $"No se puede poner en curso un retiro en estado {Estado}.");

    /// <summary>Retirado: el repartidor tiene los paquetes.</summary>
    public void Completar(Guid? envioAsociadoId = null)
    {
        Pasar(EstadoRetiro.Completado, $"El retiro ya está {Estado}.");
        EnvioAsociadoId = envioAsociadoId;
    }

    /// <summary>El repartidor fue y no pudo retirar (RF 38); se puede volver a programar.</summary>
    public void NoRetirar(string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new ArgumentException("Indicá por qué no se retiró.", nameof(motivo));
        Pasar(EstadoRetiro.NoRetirado, $"No se puede marcar como no retirado un retiro en estado {Estado}.");
        MotivoNoRetiro = motivo.Trim().Length > 300 ? motivo.Trim()[..300] : motivo.Trim();
    }

    /// <summary>Los paquetes retirados llegaron al depósito.</summary>
    public void RecibirEnDeposito() =>
        Pasar(EstadoRetiro.EnDeposito, "Solo se recibe en depósito un retiro ya retirado.");

    public void Cancelar(string motivo)
    {
        if (Estado is EstadoRetiro.Completado or EstadoRetiro.EnDeposito)
            throw new InvalidOperationException("No se puede cancelar un retiro completado.");
        Pasar(EstadoRetiro.Cancelado, $"No se puede cancelar un retiro en estado {Estado}.");
        if (!string.IsNullOrWhiteSpace(motivo))
            Observaciones = string.IsNullOrWhiteSpace(Observaciones)
                ? motivo.Trim()
                : $"{Observaciones} | Cancelado: {motivo.Trim()}";
    }

    private void Pasar(EstadoRetiro nuevo, string error)
    {
        if (!TransicionesRetiro.EsValida(Estado, nuevo))
            throw new InvalidOperationException(error);
        Estado = nuevo;
    }
}
