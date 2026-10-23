using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>
/// Pasa todos los envíos AsignadoARuta de una ruta a EnTransito en una sola transacción.
/// </summary>
public sealed class IniciarRuta
{
    private readonly IEnvioRepositorio _envios;
    private readonly RegistrarOutboxEstado _outbox;
    private readonly PublicarOutboxPendientes _publicar;
    private readonly ICacheLectura _cache;

    public IniciarRuta(
        IEnvioRepositorio envios,
        RegistrarOutboxEstado outbox,
        PublicarOutboxPendientes publicar,
        ICacheLectura cache)
    {
        _envios = envios;
        _outbox = outbox;
        _publicar = publicar;
        _cache = cache;
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

        var envios = await _envios.ObtenerPorRutaParaEditarAsync(operadorId, rutaId, cancellationToken);
        var pendientes = envios.Where(envio => envio.Estado == EstadoEnvio.AsignadoARuta).ToList();
        if (pendientes.Count == 0)
            throw new InvalidOperationException("La ruta no tiene envíos pendientes de salir.");

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
                    operadorId, envio.Id, anterior, envio.Estado, cancellationToken);
                if (eventId is Guid id)
                    eventIds.Add(id);
                iniciados++;
            }
            catch (InvalidOperationException ex)
            {
                errores.Add($"{envio.NumeroSeguimiento}: {ex.Message}");
            }
        }

        if (iniciados == 0)
            throw new InvalidOperationException(string.Join(" ", errores));

        await _envios.GuardarAsync(cancellationToken);
        foreach (var eventId in eventIds)
            await _publicar.EjecutarUnoAsync(eventId, cancellationToken);

        foreach (var envio in pendientes.Where(e => e.Estado == EstadoEnvio.EnTransito))
            await CacheSeguimiento.PublicarAsync(_cache, envio, cancellationToken);

        return new ResultadoInicioRuta(iniciados, errores);
    }
}

public sealed record ResultadoInicioRuta(int Iniciados, IReadOnlyList<string> Errores);
