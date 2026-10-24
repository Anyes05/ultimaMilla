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
