namespace Plataforma.Avisos;

/// <summary>Misma sección "RabbitMq" que usan Backoffice y Portal (RabbitMq__Host, __Username, __Password).</summary>
public sealed class OpcionesRabbitMq
{
    public const string Seccion = "RabbitMq";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>Exchange topic que declara la API al publicar el outbox (contracts/mensajes/v1).</summary>
    public string Exchange { get; set; } = "plataforma.eventos";

    public bool Configurado => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>Correo al destinatario (RF 27). Sin SmtpHost, el worker registra el mensaje en el log en lugar de mandarlo.</summary>
public sealed class OpcionesNotificaciones
{
    public const string Seccion = "Notificaciones";

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPuerto { get; set; } = 587;
    public string SmtpUsuario { get; set; } = string.Empty;
    public string SmtpClave { get; set; } = string.Empty;
    public bool SmtpSsl { get; set; } = true;
    public string Remitente { get; set; } = "no-responder@tallernet.cloud-ip.cc";

    /// <summary>Base del enlace público que recibe el destinatario.</summary>
    public string UrlSeguimiento { get; set; } = "https://tracking.tallernet.cloud-ip.cc";

    public bool SmtpConfigurado => !string.IsNullOrWhiteSpace(SmtpHost);
}

public sealed class OpcionesAvisos
{
    public const string Seccion = "Avisos";

    private static readonly int[] EsperasPorDefecto = [5, 15, 45];

    /// <summary>
    /// Espera creciente entre reintentos automáticos, en segundos. Cada valor es una cola de espera con TTL.
    /// Cambiar los valores crea colas nuevas (el nombre incluye los segundos).
    /// </summary>
    public int[]? EsperasSegundos { get; set; }

    public int TimeoutHttpSegundos { get; set; } = 10;

    public ushort Prefetch { get; set; } = 10;

    /// <summary>
    /// Hosts a los que se permite avisar por http (sin TLS), p. ej. el receptor de prueba dentro del cluster.
    /// Cualquier otra URL debe ser https: evita que un comercio use el worker para llamar servicios internos.
    /// </summary>
    public string[]? HostsHttpPermitidos { get; set; }

    public IReadOnlyList<int> Esperas =>
        EsperasSegundos is { Length: > 0 } ? EsperasSegundos.Where(segundos => segundos > 0).ToArray() : EsperasPorDefecto;

    public int MaximoReintentos => Esperas.Count;

    public bool UrlPermitida(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps ||
         (uri.Scheme == Uri.UriSchemeHttp &&
          (HostsHttpPermitidos ?? []).Contains(uri.Host, StringComparer.OrdinalIgnoreCase)));
}
