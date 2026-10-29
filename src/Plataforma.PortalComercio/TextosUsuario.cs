namespace Plataforma.PortalComercio;

/// <summary>Ayudas para mostrar al comercio textos legibles (errores y fechas).</summary>
public static class TextosUsuario
{
    /// <summary>
    /// Mensaje de una excepción de negocio sin el agregado técnico " (Parameter 'x')" que .NET
    /// suma a ArgumentException cuando se indica el nombre del parámetro.
    /// </summary>
    public static string Error(Exception ex) =>
        ex is ArgumentException { ParamName: { } parametro } argumento
            ? argumento.Message.Replace($" (Parameter '{parametro}')", string.Empty)
            : ex.Message;

    /// <summary>Fecha UTC en la zona horaria del operador (Operador.ZonaHoraria); si no existe, Uruguay (UTC-3).</summary>
    public static string HoraOperador(DateTime utc, string? zonaHoraria, string formato = "dd/MM HH:mm")
    {
        TimeZoneInfo zona;
        try
        {
            zona = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(zonaHoraria) ? "America/Montevideo" : zonaHoraria);
        }
        catch (Exception excepcion) when (excepcion is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zona = TimeZoneInfo.CreateCustomTimeZone("UY", TimeSpan.FromHours(-3), "Uruguay", "Uruguay");
        }

        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zona).ToString(formato);
    }
}
