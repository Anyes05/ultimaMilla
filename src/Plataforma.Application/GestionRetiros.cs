using System.Diagnostics;
using Plataforma.Application.Mensajes;
using Plataforma.Domain;

namespace Plataforma.Application;

public interface IRetiroRepositorio
{
    Task AgregarAsync(SolicitudRetiro retiro, CancellationToken cancellationToken = default);
    Task<SolicitudRetiro?> ObtenerAsync(Guid operadorId, Guid retiroId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SolicitudRetiro>> ListarAsync(
        Guid operadorId,
        Guid? comercioId,
        CancellationToken cancellationToken = default);
    Task GuardarAsync(CancellationToken cancellationToken = default);
}

public sealed class GestionRetiros
{
    private readonly IRetiroRepositorio _retiros;
    private readonly IBanderasOperador _banderas;
    private readonly IOutboxRepositorio _outbox;
    private readonly PublicarOutboxPendientes _publicar;
    private readonly IComercioRepositorio _comercios;

    public GestionRetiros(
        IRetiroRepositorio retiros,
        IBanderasOperador banderas,
        IOutboxRepositorio outbox,
        PublicarOutboxPendientes publicar,
        IComercioRepositorio comercios)
    {
        _comercios = comercios;
        _retiros = retiros;
        _banderas = banderas;
        _outbox = outbox;
        _publicar = publicar;
    }

    public async Task<SolicitudRetiro> SolicitarAsync(
        Guid operadorId,
        Guid comercioId,
        string direccion,
        string contacto,
        DateOnly fecha,
        string observaciones = "",
        CancellationToken cancellationToken = default)
    {
        if (!await _banderas.EstaActivaAsync(operadorId, CatalogoBanderas.RetiroDomicilio, cancellationToken))
            throw new InvalidOperationException("El retiro en domicilio no está habilitado para este operador.");

        if (!await _comercios.EstaActivoAsync(comercioId, cancellationToken))
            throw new InvalidOperationException("El comercio está dado de baja y no puede pedir retiros.");

        var retiro = SolicitudRetiro.Solicitar(
            Guid.NewGuid(), operadorId, comercioId, direccion, contacto, fecha, observaciones);
        await _retiros.AgregarAsync(retiro, cancellationToken);

        var evento = RetiroSolicitado.Crear(operadorId, retiro.Id, comercioId, retiro.Direccion);
        await _outbox.AgregarAsync(
            MensajeOutbox.CrearPendiente(
                evento.EventId,
                operadorId,
                retiro.Id,
                RetiroSolicitado.Tipo,
                RetiroSolicitado.Version,
                evento.Serializar(),
                evento.Fecha,
                Activity.Current?.Id),
            cancellationToken);

        await _retiros.GuardarAsync(cancellationToken);
        await _publicar.EjecutarUnoAsync(evento.EventId, cancellationToken);
        return retiro;
    }

    public Task<IReadOnlyList<SolicitudRetiro>> ListarAsync(
        Guid operadorId,
        Guid? comercioId = null,
        CancellationToken cancellationToken = default) =>
        _retiros.ListarAsync(operadorId, comercioId, cancellationToken);

    public async Task ProgramarAsync(
        Guid operadorId,
        Guid retiroId,
        Guid rutaId,
        CancellationToken cancellationToken = default)
    {
        var retiro = await _retiros.ObtenerAsync(operadorId, retiroId, cancellationToken)
            ?? throw new InvalidOperationException("El retiro no existe.");
        retiro.ProgramarEnRuta(rutaId);
        await _retiros.GuardarAsync(cancellationToken);
    }

    public async Task CompletarAsync(
        Guid operadorId,
        Guid retiroId,
        Guid? envioAsociadoId = null,
        CancellationToken cancellationToken = default)
    {
        var retiro = await _retiros.ObtenerAsync(operadorId, retiroId, cancellationToken)
            ?? throw new InvalidOperationException("El retiro no existe.");
        retiro.Completar(envioAsociadoId);

        var evento = RetiroCompletado.Crear(operadorId, retiro.Id, retiro.ComercioId, envioAsociadoId);
        await _outbox.AgregarAsync(
            MensajeOutbox.CrearPendiente(
                evento.EventId,
                operadorId,
                retiro.Id,
                RetiroCompletado.Tipo,
                RetiroCompletado.Version,
                evento.Serializar(),
                evento.Fecha,
                Activity.Current?.Id),
            cancellationToken);

        await _retiros.GuardarAsync(cancellationToken);
        await _publicar.EjecutarUnoAsync(evento.EventId, cancellationToken);
    }

    public async Task CancelarAsync(
        Guid operadorId,
        Guid retiroId,
        string motivo = "",
        CancellationToken cancellationToken = default)
    {
        var retiro = await _retiros.ObtenerAsync(operadorId, retiroId, cancellationToken)
            ?? throw new InvalidOperationException("El retiro no existe.");
        retiro.Cancelar(motivo);
        await _retiros.GuardarAsync(cancellationToken);
    }
}
