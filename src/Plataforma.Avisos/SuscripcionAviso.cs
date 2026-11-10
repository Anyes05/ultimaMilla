namespace Plataforma.Avisos;

/// <summary>
/// Suscripción de un comercio a los avisos automáticos (letra 6.9):
/// a qué URL se notifica, con qué secreto se firma y qué tipos de evento le interesan.
/// </summary>
public sealed class SuscripcionAviso
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Url { get; private set; } = string.Empty;

    /// <summary>Secreto compartido con el comercio para la firma HMAC del aviso.</summary>
    public string Secreto { get; private set; } = string.Empty;

    /// <summary>Tipos de evento separados por coma (estado nuevo del envío). Vacío = todos.</summary>
    public string TiposEvento { get; private set; } = string.Empty;

    public bool Activa { get; private set; }

    /// <summary>
    /// Versión del contrato que recibe (RF 36): 1 = formato original (por defecto, así nadie que ya integró cambia),
    /// 2 = sobre versionado con los estados y eventos del cambio. Ver <see cref="ContratoAvisos"/>.
    /// </summary>
    public int VersionContrato { get; private set; } = ContratoAvisos.V1;

    public DateTime CreadaEn { get; private set; }
    public DateTime ActualizadaEn { get; private set; }

    /// <summary>Concurrencia optimista (xmin en Postgres): dos ediciones simultáneas no se pisan.</summary>
    public uint Version { get; private set; }

    private SuscripcionAviso()
    {
    }

    public static SuscripcionAviso Crear(
        Guid operadorId,
        Guid comercioId,
        string url,
        string secreto,
        IEnumerable<string> tiposEvento,
        bool activa,
        DateTime ahoraUtc,
        int versionContrato = ContratoAvisos.V1)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("La suscripción necesita un operador.", nameof(operadorId));

        if (comercioId == Guid.Empty)
            throw new ArgumentException("La suscripción necesita un comercio.", nameof(comercioId));

        var suscripcion = new SuscripcionAviso
        {
            Id = Guid.NewGuid(),
            OperadorId = operadorId,
            ComercioId = comercioId,
            CreadaEn = ahoraUtc
        };
        suscripcion.Actualizar(url, secreto, tiposEvento, activa, ahoraUtc, versionContrato);
        return suscripcion;
    }

    public void Actualizar(
        string url,
        string secreto,
        IEnumerable<string> tiposEvento,
        bool activa,
        DateTime ahoraUtc,
        int versionContrato = ContratoAvisos.V1)
    {
        if (!ContratoAvisos.EsValida(versionContrato))
            throw new ArgumentException("La versión del aviso debe ser 1 o 2.", nameof(versionContrato));

        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("La URL del aviso debe ser absoluta, http o https.", nameof(url));

        if (string.IsNullOrWhiteSpace(secreto) || secreto.Trim().Length < 16)
            throw new ArgumentException("El secreto de firma debe tener al menos 16 caracteres.", nameof(secreto));

        Url = uri.ToString();
        Secreto = secreto.Trim();
        VersionContrato = versionContrato;
        TiposEvento = string.Join(',', NormalizarTipos(tiposEvento, versionContrato));
        Activa = activa;
        ActualizadaEn = ahoraUtc;
    }

    public IReadOnlyList<string> Tipos =>
        string.IsNullOrWhiteSpace(TiposEvento) ? [] : TiposEvento.Split(',');

    /// <summary>
    /// True si el aviso de este tipo (estado nuevo del envío) corresponde a la suscripción.
    /// Sin tipos elegidos recibe todos los de SU versión, no cualquier estado: un estado nuevo
    /// (por ejemplo EntregadoParcialmente) no le llega a un comercio que integró el formato v1 (RF 36).
    /// </summary>
    public bool Acepta(string tipoEvento) =>
        Activa &&
        ContratoAvisos.Tipos(VersionContrato).Contains(tipoEvento, StringComparer.OrdinalIgnoreCase) &&
        (Tipos.Count == 0 || Tipos.Contains(tipoEvento, StringComparer.OrdinalIgnoreCase));

    private static IEnumerable<string> NormalizarTipos(IEnumerable<string> tipos, int versionContrato) =>
        (tipos ?? [])
            .Select(tipo => tipo.Trim())
            .Where(tipo => ContratoAvisos.Tipos(versionContrato).Contains(tipo, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tipo => tipo, StringComparer.Ordinal);
}

/// <summary>
/// Tipos de evento que un comercio puede elegir: el estado al que pasa el envío.
/// Son los valores de texto de EstadoEnvio que viajan en el contrato v1 (contracts/mensajes/v1).
/// Es el catálogo cerrado de v1: los estados que se agreguen después no se suman acá, van al contrato v2.
/// </summary>
public static class TiposDeEvento
{
    public static readonly IReadOnlyList<string> Todos =
    [
        "EnDeposito", "AsignadoARuta", "EnTransito", "Entregado", "NoEntregado",
        "Reprogramado", "EnDevolucion", "Devuelto", "Extraviado"
    ];

    public static string Nombre(string tipo) => tipo switch
    {
        "Admitido" => "Registrado",
        "EnDeposito" => "En depósito",
        "AsignadoARuta" => "Asignado a ruta",
        "EnTransito" => "En tránsito",
        "Entregado" => "Entregado",
        "NoEntregado" => "No entregado",
        "Reprogramado" => "Reprogramado",
        "EnDevolucion" => "En devolución",
        "EntregadoParcialmente" => "Entregado parcialmente",
        "Devuelto" => "Devuelto",
        "Extraviado" => "Extraviado",
        "BultoEstadoCambiado" => "Cambio de estado de bulto",
        "RetiroSolicitado" => "Retiro solicitado",
        "RetiroCompletado" => "Retiro completado",
        _ => tipo
    };
}
