using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>
/// Pasa todos los envíos AsignadoARuta de una ruta a EnTransito en una sola transacción.
/// </summary>
public sealed class IniciarRuta
{
    private readonly IEnvioRepositorio _envios;
    private readonly IRutaRepositorio _rutas;
    private readonly RegistrarOutboxEstado _outbox;
    private readonly PublicarOutboxPendientes _publicar;
    private readonly ICacheLectura _cache;
    private readonly IRetiroRepositorio? _retiros;

    public IniciarRuta(
        IEnvioRepositorio envios,
        IRutaRepositorio rutas,
        RegistrarOutboxEstado outbox,
        PublicarOutboxPendientes publicar,
        ICacheLectura cache,
        IRetiroRepositorio? retiros = null)
    {
        _envios = envios;
        _rutas = rutas;
        _outbox = outbox;
        _publicar = publicar;
        _cache = cache;
        _retiros = retiros;
    }

    public async Task<ResultadoInicioRuta> EjecutarAsync(
        Guid operadorId,
        Guid rutaId,
        string? usuario = null,
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        if (rutaId == Guid.Empty)
            throw new ArgumentException("La ruta es requerida.", nameof(rutaId));

        var ruta = await _rutas.ObtenerAsync(operadorId, rutaId, cancellationToken)
            ?? throw new InvalidOperationException("La ruta no existe para ese operador.");

        foreach (var otra in (await _rutas.ObtenerDelDiaAsync(operadorId, ruta.Fecha, cancellationToken))
            .Where(item => item.RepartidorId == ruta.RepartidorId && item.Id != ruta.Id))
        {
            var otrosEnvios = await _envios.ObtenerPorRutaAsync(operadorId, otra.Id, cancellationToken);
            if (otrosEnvios.Any(envio => envio.Estado == EstadoEnvio.EnTransito) ||
                (_retiros is not null && (await _retiros.ListarAsync(operadorId, null, cancellationToken))
                    .Any(retiro => retiro.RutaId == otra.Id && retiro.Estado == EstadoRetiro.EnCurso)))
                throw new InvalidOperationException("El repartidor debe terminar su ruta en curso antes de iniciar otra.");
        }
        var envios = await _envios.ObtenerPorRutaParaEditarAsync(operadorId, rutaId, cancellationToken);
        var pendientes = envios.Where(envio => envio.Estado == EstadoEnvio.AsignadoARuta).ToList();
        var retirosProgramados = _retiros is null ? [] :
            (await _retiros.ListarAsync(operadorId, null, cancellationToken))
                .Where(retiro => retiro.RutaId == ruta.Id && retiro.Estado == EstadoRetiro.Programado).ToList();
        if (ruta.IniciadaEn is not null && pendientes.Count == 0 && retirosProgramados.Count == 0)
            return new ResultadoInicioRuta(0, []);
        if (pendientes.Count == 0 && retirosProgramados.Count == 0 &&
            !envios.Any(envio => envio.Estado == EstadoEnvio.EnTransito))
            throw new InvalidOperationException("La ruta no tiene envíos ni retiros pendientes de salir.");

        var iniciados = 0;
        var errores = new List<string>();
        var eventIds = new List<Guid>();

        foreach (var envio in pendientes)
        {
            try
            {
                var anterior = envio.Estado;
                if (!string.IsNullOrWhiteSpace(usuario))
                    envio.CambiarEstado(EstadoEnvio.EnTransito, usuario, "Inicio de ruta");
                else
                    envio.CambiarEstado(EstadoEnvio.EnTransito);
                var eventId = await _outbox.EjecutarAsync(
                    operadorId, envio.Id, anterior, envio.Estado, cancellationToken,
                    motivo: "Inicio de ruta", responsable: usuario ?? "");
                if (eventId is Guid id)
                    eventIds.Add(id);
                iniciados++;
            }
            catch (InvalidOperationException ex)
            {
                errores.Add($"{envio.NumeroSeguimiento}: {ex.Message}");
            }
        }

        if (iniciados == 0 && pendientes.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errores));

        foreach (var retiro in retirosProgramados)
            (await _retiros!.ObtenerAsync(operadorId, retiro.Id, cancellationToken))?.MarcarEnCurso();
        ruta.Iniciar();
        await _envios.GuardarAsync(cancellationToken);
        foreach (var eventId in eventIds)
            await _publicar.EjecutarUnoAsync(eventId, cancellationToken);

        await _cache.QuitarAsync(ObtenerRutasDeHoy.Clave(operadorId, ruta.RepartidorId, ruta.Fecha), cancellationToken);

        foreach (var envio in pendientes.Where(e => e.Estado == EstadoEnvio.EnTransito))
            await CacheSeguimiento.PublicarAsync(_cache, envio, cancellationToken);

        return new ResultadoInicioRuta(iniciados, errores);
    }
}

public sealed record ResultadoInicioRuta(int Iniciados, IReadOnlyList<string> Errores);
