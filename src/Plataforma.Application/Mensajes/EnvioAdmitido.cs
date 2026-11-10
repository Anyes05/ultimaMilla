using System.Text.Json;
using Plataforma.Domain;

namespace Plataforma.Application.Mensajes;

/// <summary>
/// Contrato v1: se dio de alta un envío. Admitido es el estado inicial, no un cambio de estado, por eso es un evento
/// aparte y los consumidores de <see cref="EnvioEstadoCambiado"/> (avisos, historial, tablero) no lo reciben.
/// Ver <c>contracts/mensajes/v1/</c>.
/// </summary>
public sealed record EnvioAdmitido(
    Guid EventId,
    DateTimeOffset Fecha,
    Guid OperadorId,
    Guid EnvioId,
    string EstadoAnterior,
    EstadoEnvio EstadoNuevo)
{
    public const string Tipo = MensajeOutbox.TipoEnvioAdmitido;
    public const string Version = MensajeOutbox.VersionContratoV1;

    public static EnvioAdmitido Crear(Guid operadorId, Guid envioId) =>
        new(Guid.NewGuid(), RelojOperacion.Ahora(), operadorId, envioId, string.Empty, EstadoEnvio.Admitido);

    public string Serializar() =>
        JsonSerializer.Serialize(this with { Fecha = RelojOperacion.ParaContrato(Fecha) }, ObtenerRutasDeHoy.OpcionesJson);
}
