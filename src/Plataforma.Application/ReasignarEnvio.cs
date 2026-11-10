using System.Diagnostics;
using Plataforma.Application.Mensajes;
using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>
/// Reasigna un envío a otro repartidor (misma jornada): una sola ruta activa + outbox <see cref="EnvioReasignado"/>.
/// </summary>
public sealed class ReasignarEnvio
{
    private readonly IEnvioRepositorio _envios;
    private readonly IRutaRepositorio _rutas;
    private readonly IRepartidorRepositorio _repartidores;
    private readonly IOutboxRepositorio _outbox;
    private readonly PublicarOutboxPendientes _publicar;
    private readonly ICacheLectura _cache;

    public ReasignarEnvio(
        IEnvioRepositorio envios,
        IRutaRepositorio rutas,
        IRepartidorRepositorio repartidores,
        IOutboxRepositorio outbox,
        PublicarOutboxPendientes publicar,
        ICacheLectura cache)
    {
        _envios = envios;
        _rutas = rutas;
        _repartidores = repartidores;
        _outbox = outbox;
        _publicar = publicar;
        _cache = cache;
    }

    public async Task<ResultadoReasignacion> EjecutarAsync(
        Guid operadorId,
        Guid envioId,
        Guid repartidorNuevoId,
        DateOnly fecha,
        bool urgente = true,
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));
        if (envioId == Guid.Empty)
            throw new ArgumentException("El envío es requerido.", nameof(envioId));

        var envio = await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El envío no existe para ese operador.");

        if (envio.RutaId is not Guid rutaAnteriorId)
            throw new InvalidOperationException("El envío no está en una ruta; despachalo antes de reasignar.");

        var rutaAnterior = await _rutas.ObtenerAsync(operadorId, rutaAnteriorId, cancellationToken)
            ?? throw new InvalidOperationException("La ruta actual del envío no existe.");

        var repartidorNuevo = await _repartidores.ObtenerAsync(repartidorNuevoId, operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El repartidor nuevo no existe para ese operador.");
        if (!repartidorNuevo.Activo)
            throw new InvalidOperationException("El repartidor nuevo no está activo.");

        if (rutaAnterior.RepartidorId == repartidorNuevoId)
            throw new InvalidOperationException("El envío ya está asignado a ese repartidor.");

        var rutasNuevas = await _rutas.ObtenerDeHoyAsync(operadorId, repartidorNuevoId, fecha, cancellationToken);
        Ruta rutaNueva;
        var creaRuta = false;
        if (rutasNuevas.Count > 0)
        {
            rutaNueva = rutasNuevas[0];
        }
        else
        {
            rutaNueva = Ruta.Programar(operadorId, repartidorNuevoId, fecha);
            creaRuta = true;
        }

        var enviosEnNueva = creaRuta
            ? []
            : await _envios.ObtenerPorRutaAsync(operadorId, rutaNueva.Id, cancellationToken);
        var orden = enviosEnNueva.Count + 1;

        var paradaAnterior = await _rutas.ObtenerParadaPorEnvioAsync(operadorId, envio.Id, cancellationToken);
        if (paradaAnterior is not null)
            await _rutas.QuitarParadaAsync(paradaAnterior, cancellationToken);

        envio.ReasignarARuta(rutaNueva.Id);

        await _rutas.AgregarParadaAsync(
            Parada.Programar(Guid.NewGuid(), operadorId, rutaNueva.Id, envio.Id, orden),
            cancellationToken);

        var evento = EnvioReasignado.Crear(
            operadorId,
            envio.Id,
            envio.NumeroSeguimiento,
            rutaAnterior.RepartidorId,
            repartidorNuevoId,
            rutaNueva.Id,
            urgente);

        await _outbox.AgregarAsync(
            MensajeOutbox.CrearPendiente(
                evento.EventId,
                operadorId,
                envio.Id,
                EnvioReasignado.Tipo,
                EnvioReasignado.Version,
                evento.Serializar(),
                evento.Fecha,
                Activity.Current?.Id),
            cancellationToken);

        if (creaRuta)
            await _rutas.GuardarDespachoAsync(rutaNueva, [], cancellationToken);
        else
            await _rutas.GuardarAsync(cancellationToken);

        await _envios.GuardarAsync(cancellationToken);
        await _publicar.EjecutarUnoAsync(evento.EventId, cancellationToken);

        await _cache.QuitarAsync(
            ObtenerRutasDeHoy.Clave(operadorId, rutaAnterior.RepartidorId, rutaAnterior.Fecha),
            cancellationToken);
        await _cache.QuitarAsync(
            ObtenerRutasDeHoy.Clave(operadorId, repartidorNuevoId, fecha),
            cancellationToken);
        await CacheSeguimiento.PublicarAsync(_cache, envio, cancellationToken);

        return new ResultadoReasignacion(
            envio.Id,
            rutaAnterior.RepartidorId,
            repartidorNuevoId,
            rutaNueva.Id,
            evento.EventId);
    }
}

public sealed record ResultadoReasignacion(
    Guid EnvioId,
    Guid RepartidorAnteriorId,
    Guid RepartidorNuevoId,
    Guid RutaNuevaId,
    Guid EventId);
