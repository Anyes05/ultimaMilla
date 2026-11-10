using System.Text.Json;
using Plataforma.Domain;

namespace Plataforma.Application.Mensajes;

/// <summary>
/// Contrato v1: un envío se reasignó a otro repartidor/ruta (push urgente, opcional 7.3).
/// Ver <c>contracts/mensajes/v1/</c> y <c>docs/propuestas/push-reasignacion.md</c>.
/// </summary>
public sealed record EnvioReasignado(
    Guid EventId,
    DateTimeOffset Fecha,
    Guid OperadorId,
    Guid EnvioId,
    string? NumeroSeguimiento,
    Guid? RepartidorAnteriorId,
    Guid RepartidorNuevoId,
    Guid? RutaNuevaId,
    bool Urgente)
{
    public const string Tipo = MensajeOutbox.TipoEnvioReasignado;
    public const string Version = MensajeOutbox.VersionContratoV1;

    public static EnvioReasignado Crear(
        Guid operadorId,
        Guid envioId,
        string? numeroSeguimiento,
        Guid? repartidorAnteriorId,
        Guid repartidorNuevoId,
        Guid? rutaNuevaId,
        bool urgente = true) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            operadorId,
            envioId,
            numeroSeguimiento,
            repartidorAnteriorId,
            repartidorNuevoId,
            rutaNuevaId,
            urgente);

    public string Serializar() =>
        JsonSerializer.Serialize(this, ObtenerRutasDeHoy.OpcionesJson);
}
