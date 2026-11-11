namespace Plataforma.Domain;

public class ReglaOperativa
{
    public static IReadOnlyList<string> MotivosNoEntregaPredeterminados { get; } =
        ["Ausente", "Destinatario ausente", "Rechazo del destinatario", "Domicilio no encontrado", "Bulto danado"];

    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public int MaximoIntentos { get; private set; }
    public int PlazoEntregaHoras { get; private set; }
    public bool ExigeFirma { get; private set; }
    public bool ExigeFoto { get; private set; }
    public bool ExigeGeocerca { get; private set; }
    public int PlazoReprogramacionHoras { get; private set; }
    public bool AdmiteEntregaParcial { get; private set; }
    public PoliticaBultosPendientes PoliticaBultosPendientes { get; private set; }
    public string[] MotivosNoEntrega { get; private set; } = [];

    /// <summary>RF 20: además de nombre, firma y foto, el operador puede exigir el documento de quien recibe.</summary>
    public bool ExigeDocumentoReceptor { get; private set; }

    /// <summary>Horas mínimas entre un intento fallido y la siguiente salida del envío (0 = sin espera).</summary>
    public int PlazoEntreIntentosHoras { get; private set; }

    /// <summary>Qué pasa con el envío al agotar los intentos.</summary>
    public PoliticaDevolucion PoliticaDevolucion { get; private set; }

    /// <summary>Plazo comprometido para envíos urgentes; sin valor, rige el mismo que para los estándar.</summary>
    public int? PlazoEntregaUrgenteHoras { get; private set; }
    public DateTime VigenciaDesde { get; private set; }
    public DateTime VigenciaHasta { get; private set; }
    public bool Activa { get; private set; }

    private ReglaOperativa()
    {
    }

    /// <summary>
    /// Termina esta versión al publicarse otra. Sigue activa para su período: un envío procesado bajo ella
    /// conserva su tratamiento (letra 6.6).
    /// </summary>
    public void CerrarVigencia(DateTime hastaUtc)
    {
        if (hastaUtc < VigenciaDesde)
            throw new ArgumentException("La vigencia hasta no puede ser anterior al desde.");

        VigenciaHasta = hastaUtc;
    }

    /// <summary>
    /// RF 26: el destinatario puede pedir otra visita después de un intento fallido, si quedan intentos
    /// y todavía está dentro del plazo de reprogramación del operador.
    /// </summary>
    public bool PermiteReprogramar(EstadoEnvio estado, int intentosRealizados, DateTime? ultimoIntentoUtc, DateTime ahoraUtc) =>
        estado == EstadoEnvio.NoEntregado
        && intentosRealizados < MaximoIntentos
        && ultimoIntentoUtc is { } intento
        && ahoraUtc <= intento.AddHours(PlazoReprogramacionHoras);

    /// <summary>Plazo comprometido según la modalidad del envío (RF 4: plazos por modalidad de servicio).</summary>
    public int PlazoEntregaPara(bool urgente) =>
        urgente && PlazoEntregaUrgenteHoras is int horas ? horas : PlazoEntregaHoras;

    public bool DevuelveAlAgotarIntentos => PoliticaDevolucion == PoliticaDevolucion.Automatica;

    public static ReglaOperativa Crear(
        Guid id,
        Guid operadorId,
        int maximoIntentos,
        int plazoEntregaHoras,
        bool exigeFirma,
        bool exigeFoto,
        bool exigeGeocerca,
        int plazoReprogramacionHoras,
        DateTime vigenciaDesde,
        DateTime vigenciaHasta,
        bool admiteEntregaParcial = false,
        PoliticaBultosPendientes politicaBultosPendientes = PoliticaBultosPendientes.NuevaVisitaSinRecargo,
        IReadOnlyList<string>? motivosNoEntrega = null,
        bool exigeDocumentoReceptor = false,
        int plazoEntreIntentosHoras = 0,
        PoliticaDevolucion politicaDevolucion = PoliticaDevolucion.Automatica,
        int? plazoEntregaUrgenteHoras = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La regla operativa necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La regla operativa necesita un operador.");

        if (maximoIntentos < 1)
            throw new ArgumentOutOfRangeException(nameof(maximoIntentos), "El máximo de intentos debe ser al menos 1.");

        if (vigenciaHasta < vigenciaDesde)
            throw new ArgumentException("La vigencia hasta no puede ser anterior al desde.");

        if (plazoEntreIntentosHoras is < 0 or > 720)
            throw new ArgumentOutOfRangeException(nameof(plazoEntreIntentosHoras), "El plazo entre intentos debe estar entre 0 y 720 horas.");

        if (plazoEntregaUrgenteHoras is < 1 or > 720)
            throw new ArgumentOutOfRangeException(nameof(plazoEntregaUrgenteHoras), "El plazo de entrega urgente debe estar entre 1 y 720 horas.");

        var catalogoMotivos = (motivosNoEntrega ?? MotivosNoEntregaPredeterminados)
            .Select(motivo => motivo.Trim())
            .Where(motivo => motivo.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (catalogoMotivos.Length == 0 || catalogoMotivos.Any(motivo => motivo.Length > 100))
            throw new ArgumentException("Configura al menos un motivo de no entrega de hasta 100 caracteres.", nameof(motivosNoEntrega));

        return new ReglaOperativa
        {
            Id = id,
            OperadorId = operadorId,
            MaximoIntentos = maximoIntentos,
            PlazoEntregaHoras = plazoEntregaHoras,
            ExigeFirma = exigeFirma,
            ExigeFoto = exigeFoto,
            ExigeGeocerca = exigeGeocerca,
            PlazoReprogramacionHoras = plazoReprogramacionHoras,
            AdmiteEntregaParcial = admiteEntregaParcial,
            PoliticaBultosPendientes = politicaBultosPendientes,
            MotivosNoEntrega = catalogoMotivos,
            ExigeDocumentoReceptor = exigeDocumentoReceptor,
            PlazoEntreIntentosHoras = plazoEntreIntentosHoras,
            PoliticaDevolucion = politicaDevolucion,
            PlazoEntregaUrgenteHoras = plazoEntregaUrgenteHoras,
            VigenciaDesde = vigenciaDesde,
            VigenciaHasta = vigenciaHasta,
            Activa = true
        };
    }
}

/// <summary>Política de devolución del operador al agotar los intentos de entrega (RF 4).</summary>
public enum PoliticaDevolucion
{
    /// <summary>El envío pasa solo a devolución (transición 11).</summary>
    Automatica,

    /// <summary>Queda No entregado y el operador decide desde Monitoreo si lo devuelve.</summary>
    EsperarDecision
}
