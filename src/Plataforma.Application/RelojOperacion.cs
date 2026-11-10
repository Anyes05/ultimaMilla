namespace Plataforma.Application;

/// <summary>
/// Reloj operativo de la plataforma: Uruguay (UTC−03:00) sin DST.
/// Los contratos de sync y mensajes usan offset −03:00.
/// </summary>
public static class RelojOperacion
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(-3);

    public static DateTimeOffset Ahora() => DateTimeOffset.UtcNow.ToOffset(Offset);

    public static DateTimeOffset ConOffsetOperacion(DateTimeOffset instante) => instante.ToOffset(Offset);
}
