using Plataforma.Application.Mensajes;
using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>
/// En la misma unidad de trabajo (sin SaveChanges): historial inmutable + outbox
/// <see cref="EnvioEstadoCambiado"/>.
/// </summary>
public sealed class RegistrarOutboxEstado
{
    private readonly IOutboxRepositorio _outbox;
    private readonly IHistorialEstadoRepositorio _historial;

    public RegistrarOutboxEstado(IOutboxRepositorio outbox, IHistorialEstadoRepositorio historial)
    {
        _outbox = outbox;
        _historial = historial;
    }

    public async Task<Guid?> EjecutarAsync(
        Guid operadorId,
        Guid envioId,
        EstadoEnvio estadoAnterior,
        EstadoEnvio estadoNuevo,
        CancellationToken cancellationToken = default,
        Guid? eventId = null,
        string motivo = "")
    {
        if (estadoAnterior == estadoNuevo)
            return null;

        var mensaje = EnvioEstadoCambiado.Crear(
            operadorId, envioId, estadoAnterior, estadoNuevo, eventId: eventId);

        if (await _outbox.ExisteAsync(mensaje.EventId, cancellationToken))
            throw new InvalidOperationException($"El EventId {mensaje.EventId} ya existe en outbox.");

        await _historial.AgregarAsync(
            HistorialEstado.Registrar(
                Guid.NewGuid(),
                operadorId,
                envioId,
                estadoNuevo,
                mensaje.Fecha.UtcDateTime,
                string.IsNullOrWhiteSpace(motivo)
                    ? $"{estadoAnterior} -> {estadoNuevo}"
                    : motivo),
            cancellationToken);

        var fila = MensajeOutbox.CrearPendiente(
            mensaje.EventId,
            mensaje.OperadorId,
            mensaje.EnvioId,
            EnvioEstadoCambiado.Tipo,
            EnvioEstadoCambiado.Version,
            mensaje.Serializar(),
            mensaje.Fecha);

        await _outbox.AgregarAsync(fila, cancellationToken);
        return mensaje.EventId;
    }
}
