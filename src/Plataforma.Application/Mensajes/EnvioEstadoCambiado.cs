using System.Text.Json;
using Plataforma.Domain;

namespace Plataforma.Application.Mensajes;

/// <summary>
/// Contrato mínimo de mensaje v1: cambio de estado de un envío.
/// Fuente de verdad para Nadia (worker/SignalR) y Álvaro (consumidores futuros).
/// Ver <c>contracts/mensajes/v1/</c>.
/// </summary>
public sealed record EnvioEstadoCambiado(
    Guid EventId,
    DateTimeOffset Fecha,
    Guid OperadorId,
    Guid EnvioId,
    EstadoEnvio EstadoAnterior,
    EstadoEnvio EstadoNuevo)
{
    public const string Tipo = MensajeOutbox.TipoEnvioEstadoCambiado;
    public const string Version = MensajeOutbox.VersionContratoV1;

    public static EnvioEstadoCambiado Crear(
        Guid operadorId,
        Guid envioId,
        EstadoEnvio estadoAnterior,
        EstadoEnvio estadoNuevo,
        DateTimeOffset? fecha = null,
        Guid? eventId = null) =>
        new(
            eventId ?? Guid.NewGuid(),
            fecha ?? DateTimeOffset.UtcNow,
            operadorId,
            envioId,
            estadoAnterior,
            estadoNuevo);

    public string Serializar() =>
        JsonSerializer.Serialize(this, ObtenerRutasDeHoy.OpcionesJson);

    public static EnvioEstadoCambiado? Deserializar(string json) =>
        JsonSerializer.Deserialize<EnvioEstadoCambiado>(json, ObtenerRutasDeHoy.OpcionesJson);
}
