namespace Plataforma.Application;

/// <summary>Catálogo cerrado de banderas (opcional 7.4). Una clave mal escrita no compila.</summary>
public static class CatalogoBanderas
{
    public const string EntregaParcial = "entrega-parcial";
    public const string RetiroDomicilio = "retiro-domicilio";
    public const string SeguimientoReprogramacion = "seguimiento-reprogramacion";
    public const string AvisosComercio = "avisos-comercio";

    public static readonly TimeSpan VigenciaCache = TimeSpan.FromSeconds(30);

    public static IReadOnlyList<DefinicionBandera> Todas { get; } =
    [
        new(EntregaParcial, "Entrega parcial de bultos (RF 31–36)", ActivaPorDefecto: true),
        new(RetiroDomicilio, "Retiro en domicilio (RF 37–41)", ActivaPorDefecto: false),
        new(SeguimientoReprogramacion, "Destinatario pide reprogramación (RF 26)", ActivaPorDefecto: true),
        new(AvisosComercio, "Avisos HTTP a sistemas del comercio", ActivaPorDefecto: true)
    ];

    public static bool EsConocida(string clave) =>
        Todas.Any(b => string.Equals(b.Clave, clave, StringComparison.Ordinal));

    public static bool ValorPorDefecto(string clave) =>
        Todas.FirstOrDefault(b => string.Equals(b.Clave, clave, StringComparison.Ordinal))?.ActivaPorDefecto
        ?? false;
}

public sealed record DefinicionBandera(string Clave, string Descripcion, bool ActivaPorDefecto);
