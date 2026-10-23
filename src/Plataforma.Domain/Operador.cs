namespace Plataforma.Domain;

public class Operador
{
    public const string ZonaHorariaUruguay = "America/Montevideo";

    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public string ColorMarca { get; private set; } = string.Empty;
    public int MaximoIntentos { get; private set; }
    public string ZonaHoraria { get; private set; } = ZonaHorariaUruguay;

    private Operador()
    {
    }

    public static Operador Crear(
        Guid id,
        string nombre,
        string colorMarca,
        int maximoIntentos,
        string zonaHoraria = ZonaHorariaUruguay)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El operador necesita un identificador.");

        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre del operador es requerido.");

        if (string.IsNullOrWhiteSpace(colorMarca))
            throw new ArgumentException("El color de marca es requerido.");

        if (maximoIntentos < 1)
            throw new ArgumentOutOfRangeException(nameof(maximoIntentos), "El máximo de intentos debe ser al menos 1.");

        if (string.IsNullOrWhiteSpace(zonaHoraria))
            throw new ArgumentException("La zona horaria del operador es requerida.");

        // Valida el id IANA / Windows al crear el operador.
        _ = TimeZoneInfo.FindSystemTimeZoneById(zonaHoraria.Trim());

        return new Operador
        {
            Id = id,
            Nombre = nombre.Trim(),
            ColorMarca = colorMarca.Trim().ToLowerInvariant(),
            MaximoIntentos = maximoIntentos,
            ZonaHoraria = zonaHoraria.Trim()
        };
    }

    /// <summary>Día de operación según la zona del operador (no UTC del servidor).</summary>
    public DateOnly DiaActual(DateTime? utcAhora = null)
    {
        var zona = TimeZoneInfo.FindSystemTimeZoneById(ZonaHoraria);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcAhora ?? DateTime.UtcNow, zona);
        return DateOnly.FromDateTime(local);
    }
}
