using System.Text.Json;
using Plataforma.Domain;

namespace Plataforma.Application.Mensajes;

public sealed record RetiroSolicitado(
    Guid EventId,
    DateTimeOffset Fecha,
    Guid OperadorId,
    Guid RetiroId,
    Guid ComercioId,
    string Direccion)
{
    public const string Tipo = "RetiroSolicitado";
    public const string Version = MensajeOutbox.VersionContratoV1;

    public static RetiroSolicitado Crear(Guid operadorId, Guid retiroId, Guid comercioId, string direccion) =>
        new(Guid.NewGuid(), RelojOperacion.Ahora(), operadorId, retiroId, comercioId, direccion);

    public string Serializar() =>
        JsonSerializer.Serialize(this with { Fecha = RelojOperacion.ParaContrato(Fecha) }, ObtenerRutasDeHoy.OpcionesJson);
}

public sealed record RetiroCompletado(
    Guid EventId,
    DateTimeOffset Fecha,
    Guid OperadorId,
    Guid RetiroId,
    Guid ComercioId,
    Guid? EnvioAsociadoId)
{
    public const string Tipo = "RetiroCompletado";
    public const string Version = MensajeOutbox.VersionContratoV1;

    public static RetiroCompletado Crear(
        Guid operadorId,
        Guid retiroId,
        Guid comercioId,
        Guid? envioAsociadoId) =>
        new(Guid.NewGuid(), RelojOperacion.Ahora(), operadorId, retiroId, comercioId, envioAsociadoId);

    public string Serializar() =>
        JsonSerializer.Serialize(this with { Fecha = RelojOperacion.ParaContrato(Fecha) }, ObtenerRutasDeHoy.OpcionesJson);
}

public sealed record BultoEstadoCambiado(
    Guid EventId,
    DateTimeOffset Fecha,
    Guid OperadorId,
    Guid EnvioId,
    Guid BultoId,
    string EstadoAnterior,
    string EstadoNuevo)
{
    public const string Tipo = "BultoEstadoCambiado";
    public const string Version = MensajeOutbox.VersionContratoV1;

    public static BultoEstadoCambiado Crear(
        Guid operadorId,
        Guid envioId,
        Guid bultoId,
        EstadoBulto anterior,
        EstadoBulto nuevo) =>
        new(Guid.NewGuid(), RelojOperacion.Ahora(), operadorId, envioId, bultoId, anterior.ToString(), nuevo.ToString());

    public string Serializar() =>
        JsonSerializer.Serialize(this with { Fecha = RelojOperacion.ParaContrato(Fecha) }, ObtenerRutasDeHoy.OpcionesJson);
}
