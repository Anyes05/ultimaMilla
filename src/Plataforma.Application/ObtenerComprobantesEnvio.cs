using System.Text.Json;
using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>
/// Pruebas de entrega de un envío para el Backoffice (RF 20: foto, firma, receptor, posición y hora del
/// dispositivo). Solo lectura y siempre dentro del operador de quien consulta.
/// </summary>
public sealed class ObtenerComprobantesEnvio
{
    private readonly IEnvioRepositorio _envios;
    private readonly IPruebaDeEntregaRepositorio _pruebas;

    public ObtenerComprobantesEnvio(IEnvioRepositorio envios, IPruebaDeEntregaRepositorio pruebas)
    {
        _envios = envios;
        _pruebas = pruebas;
    }

    public async Task<ComprobantesEnvio?> Ejecutar(AccesoActual acceso, Guid envioId, CancellationToken cancellationToken = default)
    {
        var envio = await _envios.ObtenerPorOperadorAsync(envioId, acceso.OperadorId, cancellationToken);
        if (envio is null)
            return null;

        var pruebas = await _pruebas.ListarPorEnvioAsync(acceso.OperadorId, envio.Id, cancellationToken);
        return new ComprobantesEnvio(
            envio.Id,
            envio.NumeroSeguimiento,
            envio.Destinatario,
            envio.DireccionEntrega,
            envio.Estado,
            pruebas.Select(ComprobanteVisible.Desde).ToList());
    }
}

public sealed record ComprobantesEnvio(
    Guid EnvioId,
    string NumeroSeguimiento,
    string Destinatario,
    string DireccionEntrega,
    EstadoEnvio Estado,
    IReadOnlyList<ComprobanteVisible> Comprobantes);

public sealed record PuntoFirma(double X, double Y);

public sealed record ComprobanteVisible(
    string? FotoBase64,
    IReadOnlyList<IReadOnlyList<PuntoFirma>> Firma,
    string NombreReceptor,
    string Observaciones,
    string Usuario,
    DateTimeOffset FechaDispositivo,
    decimal? Latitud,
    decimal? Longitud,
    string? CodigoEscaneado)
{
    public static ComprobanteVisible Desde(PruebaDeEntrega prueba) => new(
        EsBase64(prueba.Foto) ? prueba.Foto : null,
        LeerFirma(prueba.Firma),
        prueba.NombreReceptor,
        prueba.Observaciones,
        prueba.Usuario,
        prueba.FechaDispositivo,
        prueba.Latitud,
        prueba.Longitud,
        prueba.CodigoEscaneado);

    // La app manda el JPEG en base64; cualquier otra cosa no se muestra como imagen.
    private static bool EsBase64(string valor) =>
        !string.IsNullOrWhiteSpace(valor) && Convert.TryFromBase64String(valor, new byte[valor.Length], out _);

    // La firma viaja como trazos del dedo: [[{"x":1,"y":2},...],...]. Si no se puede leer, se muestra sin firma.
    private static IReadOnlyList<IReadOnlyList<PuntoFirma>> LeerFirma(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var trazos = JsonSerializer.Deserialize<List<List<PuntoFirma>>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return trazos?.Where(trazo => trazo.Count > 1).Select(trazo => (IReadOnlyList<PuntoFirma>)trazo).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
