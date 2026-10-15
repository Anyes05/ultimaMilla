using System.Text.Json;
using System.Text.Json.Serialization;
using Plataforma.Mobile.Models;

namespace Plataforma.Mobile.Services;

public sealed record LoginSolicitud(string Email, string Clave);
public sealed record LoginRespuesta(
    Guid RepartidorId, Guid OperadorId, string Nombre, string Rol, string Autenticacion);
public sealed record OperadorContrato(string Nombre, string ColorMarca, int MaximoIntentos);
public sealed record EnvioContrato(
    Guid Id, string NumeroSeguimiento, string Destinatario, string DireccionEntrega,
    string EmailDestinatario, EstadoEnvio Estado, decimal TarifaCalculada, int IntentosRealizados);
public sealed record RutaContrato(
    Guid Id, DateOnly Fecha, OperadorContrato Operador, IReadOnlyList<EnvioContrato> Envios);
public sealed record EntregaContrato(
    Guid EventoId, Guid EnvioId, EstadoEnvio Estado, string NombreReceptor,
    string Observaciones, DateTimeOffset FechaDispositivo);
public sealed record SincronizacionSolicitud(DateOnly Fecha, IReadOnlyList<EntregaContrato> Eventos);
public sealed record AplicadoContrato(Guid EnvioId, EstadoEnvio Estado, int IntentosRealizados);
public sealed record RechazadoContrato(Guid EnvioId, string Motivo, EstadoEnvio EstadoActual);
public sealed record SincronizacionRespuesta(
    IReadOnlyList<AplicadoContrato> Aplicados, IReadOnlyList<RechazadoContrato> Rechazados);

public static class ContratosRepartidor
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter<EstadoEnvio>(allowIntegerValues: false) }
    };

    public static string LeerEjemplo(string archivo)
    {
        using var stream = typeof(ContratosRepartidor).Assembly
            .GetManifestResourceStream($"Contratos.Repartidor.{archivo}")
            ?? throw new InvalidOperationException($"No se encontro el contrato incluido: {archivo}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static T Leer<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new InvalidOperationException("El contrato JSON no puede ser null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("El JSON no cumple la propuesta de contrato del repartidor.", ex);
        }
    }

    public static string Escribir<T>(T valor) => JsonSerializer.Serialize(valor, Options);
}
