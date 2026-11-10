namespace Plataforma.Avisos;

/// <summary>
/// Versiones del contrato de avisos a comercios (cambio de requerimientos, RF 36: "como los sistemas de los
/// comercios ya reciben avisos con el formato actual, el cambio no puede afectarlos, por lo que el contrato de
/// los avisos deberá versionarse").
/// <list type="bullet">
/// <item><b>v1</b>: el formato original, congelado. No se le agregan campos ni tipos de evento nuevos.</item>
/// <item><b>v2</b>: sobre con versión y número de seguimiento; suma los estados y eventos del cambio
/// (entrega parcial; luego bultos y retiros).</item>
/// </list>
/// Cada suscripción elige su versión (letra 6.9: "suscripción configurable por comercio y por tipo de evento");
/// las existentes quedan en v1. Contrato documentado en docs/contratos-api.md, sección 10.
/// </summary>
public static class ContratoAvisos
{
    public const int V1 = 1;
    public const int V2 = 2;

    /// <summary>Encabezado HTTP con la versión del cuerpo, junto a la firma.</summary>
    public const string HeaderVersion = "X-Plataforma-Version";

    /// <summary>Tipos que solo existen desde v2 (estados y eventos del cambio de requerimientos).</summary>
    public static readonly IReadOnlyList<string> AgregadosV2 =
    [
        "EntregadoParcialmente",
        "BultoEstadoCambiado",
        "RetiroSolicitado",
        "RetiroCompletado"
    ];

    public static bool EsValida(int version) => version is V1 or V2;

    /// <summary>Tipos de evento que puede recibir una suscripción de esa versión.</summary>
    public static IReadOnlyList<string> Tipos(int version) =>
        version >= V2 ? [.. TiposDeEvento.Todos, .. AgregadosV2] : TiposDeEvento.Todos;

    /// <summary>Cuerpo JSON exacto que se envía (y se firma) a una suscripción de esa versión.</summary>
    public static string Armar(int version, EventoEstadoEnvio evento, string? numeroSeguimiento) =>
        version >= V2
            ? JsonAvisos.Serializar(new AvisoComercioV2(
                V2,
                evento.EventId,
                "EnvioEstadoCambiado",
                evento.Fecha,
                new EnvioAvisoV2(evento.EnvioId, numeroSeguimiento, evento.EstadoAnterior, evento.EstadoNuevo)))
            // v1: el mismo registro y los mismos campos de siempre. No cambiar.
            : JsonAvisos.Serializar(new AvisoComercio(
                evento.EventId, "EnvioEstadoCambiado", evento.Fecha, evento.EnvioId, evento.EstadoAnterior, evento.EstadoNuevo));

    /// <summary>Aviso v2 de bulto (RF 36): solo suscripciones v2 que eligieron el tipo.</summary>
    public static string ArmarBulto(
        Guid eventId,
        DateTimeOffset fecha,
        Guid envioId,
        string? numeroSeguimiento,
        Guid bultoId,
        string estadoAnterior,
        string estadoNuevo) =>
        JsonAvisos.Serializar(new AvisoComercioV2Bulto(
            V2,
            eventId,
            "BultoEstadoCambiado",
            fecha,
            new EnvioAvisoV2(envioId, numeroSeguimiento, estadoAnterior, estadoNuevo),
            new BultoAvisoV2(bultoId, estadoAnterior, estadoNuevo)));

    /// <summary>Aviso v2 de retiro (RF 36 / RF 37–41).</summary>
    public static string ArmarRetiro(
        string tipo,
        Guid eventId,
        DateTimeOffset fecha,
        Guid retiroId,
        Guid comercioId,
        string? direccion = null,
        Guid? envioAsociadoId = null) =>
        JsonAvisos.Serializar(new AvisoComercioV2Retiro(
            V2,
            eventId,
            tipo,
            fecha,
            new RetiroAvisoV2(retiroId, comercioId, direccion, envioAsociadoId)));

    /// <summary>Lee un cuerpo de cualquier versión, como el sistema de un comercio. Lo usa el receptor de prueba.</summary>
    public static AvisoComercio? Leer(string json)
    {
        var v2 = System.Text.Json.JsonSerializer.Deserialize<AvisoComercioV2>(json, JsonAvisos.Opciones);
        return v2?.Envio is { } envio
            ? new AvisoComercio(v2.EventId, v2.Tipo, v2.Fecha, envio.Id, envio.EstadoAnterior, envio.EstadoNuevo)
            : System.Text.Json.JsonSerializer.Deserialize<AvisoComercio>(json, JsonAvisos.Opciones);
    }
}

/// <summary>Aviso v2: sobre versionado. Los eventos de bulto y de retiro se suman como otros "tipo".</summary>
public sealed record AvisoComercioV2(
    int Version,
    Guid EventId,
    string Tipo,
    DateTimeOffset Fecha,
    EnvioAvisoV2 Envio);

public sealed record EnvioAvisoV2(
    Guid Id,
    string? NumeroSeguimiento,
    string EstadoAnterior,
    string EstadoNuevo);

public sealed record BultoAvisoV2(Guid Id, string EstadoAnterior, string EstadoNuevo);

public sealed record AvisoComercioV2Bulto(
    int Version,
    Guid EventId,
    string Tipo,
    DateTimeOffset Fecha,
    EnvioAvisoV2 Envio,
    BultoAvisoV2 Bulto);

public sealed record RetiroAvisoV2(
    Guid Id,
    Guid ComercioId,
    string? Direccion,
    Guid? EnvioAsociadoId);

public sealed record AvisoComercioV2Retiro(
    int Version,
    Guid EventId,
    string Tipo,
    DateTimeOffset Fecha,
    RetiroAvisoV2 Retiro);
