using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Plataforma.Avisos;

/// <summary>
/// Firma HMAC-SHA256 de los avisos (letra 6.9): el comercio verifica con su secreto que el aviso
/// viene de la plataforma y que no fue alterado. Se firma "timestamp.cuerpo" para que una
/// captura vieja no se pueda reenviar (protección contra repetición).
/// </summary>
public static class FirmaAviso
{
    public const string HeaderFirma = "X-Plataforma-Firma";
    public const string HeaderTimestamp = "X-Plataforma-Timestamp";
    public const string HeaderEvento = "X-Plataforma-Evento";
    public const string Prefijo = "sha256=";

    public static readonly TimeSpan ToleranciaPorDefecto = TimeSpan.FromMinutes(5);

    public static string Firmar(string secreto, long timestampUnix, string cuerpo)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secreto));
        var datos = Encoding.UTF8.GetBytes($"{timestampUnix.ToString(CultureInfo.InvariantCulture)}.{cuerpo}");
        return Prefijo + Convert.ToHexStringLower(hmac.ComputeHash(datos));
    }

    /// <summary>Verificación que usa el receptor de prueba (y que debería replicar cualquier comercio).</summary>
    public static bool Verificar(
        string secreto,
        string? timestampHeader,
        string cuerpo,
        string? firmaHeader,
        DateTimeOffset ahora,
        TimeSpan? tolerancia = null)
    {
        if (string.IsNullOrWhiteSpace(firmaHeader) ||
            !long.TryParse(timestampHeader, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
            return false;

        var antiguedad = (ahora - DateTimeOffset.FromUnixTimeSeconds(timestamp)).Duration();
        if (antiguedad > (tolerancia ?? ToleranciaPorDefecto))
            return false;

        var esperada = Encoding.UTF8.GetBytes(Firmar(secreto, timestamp, cuerpo));
        var recibida = Encoding.UTF8.GetBytes(firmaHeader.Trim());
        // Comparación en tiempo constante: no filtra por timing cuántos caracteres coinciden.
        return CryptographicOperations.FixedTimeEquals(esperada, recibida);
    }
}
