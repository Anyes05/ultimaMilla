namespace Plataforma.Application;

/// <summary>
/// Reloj operativo. Npgsql solo persiste <c>timestamptz</c> en UTC (offset 0);
/// los contratos JSON de mensajes/sync se exponen en Uruguay (UTC−03:00).
/// </summary>
public static class RelojOperacion
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(-3);

    /// <summary>Ahora en UTC, apto para guardar en PostgreSQL.</summary>
    public static DateTimeOffset Ahora() => DateTimeOffset.UtcNow;

    /// <summary>Convierte cualquier instante a UTC (offset 0) antes de persistir.</summary>
    public static DateTimeOffset AUtc(DateTimeOffset instante) => instante.ToUniversalTime();

    /// <summary>Offset −03:00 para el JSON de contratos (misma instante).</summary>
    public static DateTimeOffset ParaContrato(DateTimeOffset instante) => instante.ToOffset(Offset);
}
