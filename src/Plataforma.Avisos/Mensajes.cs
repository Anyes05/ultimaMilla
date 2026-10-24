using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plataforma.Avisos;

/// <summary>
/// Lectura del contrato EnvioEstadoCambiado v1 (contracts/mensajes/v1) desde el lado del consumidor.
/// Se lee con un DTO propio y tolerante (estados como texto, campos extra ignorados) para no acoplar
/// el worker a las clases del núcleo. ComercioId es opcional: hoy el contrato no lo trae.
/// </summary>
public sealed record EventoEstadoEnvio(
    Guid EventId,
    DateTimeOffset Fecha,
    Guid OperadorId,
    Guid EnvioId,
    string EstadoAnterior,
    string EstadoNuevo,
    Guid? ComercioId = null);

/// <summary>Mensaje interno de la cola de entregas: solo el id; el estado vive en la base.</summary>
public sealed record MensajeEntrega(Guid EntregaId);

/// <summary>Resultado de un aviso, publicado para el tablero en tiempo real (routing key avisoresultado.v1).</summary>
public sealed record ResultadoAviso(
    Guid EntregaId,
    Guid EventId,
    Guid OperadorId,
    Guid ComercioId,
    Guid EnvioId,
    string TipoEvento,
    string Estado,
    int Intentos,
    string? Error,
    DateTimeOffset Fecha);

/// <summary>Cuerpo que recibe el sistema del comercio.</summary>
public sealed record AvisoComercio(
    Guid EventId,
    string Tipo,
    DateTimeOffset Fecha,
    Guid EnvioId,
    string EstadoAnterior,
    string EstadoNuevo);

public static class JsonAvisos
{
    public static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serializar<T>(T valor) => JsonSerializer.Serialize(valor, Opciones);

    public static T? Deserializar<T>(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<T>(json, Opciones);
}
