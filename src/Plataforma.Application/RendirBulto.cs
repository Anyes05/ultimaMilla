using System.Diagnostics;
using Plataforma.Application.Mensajes;
using Plataforma.Domain;

namespace Plataforma.Application;

public sealed record ResultadoRendicionBulto(
    Guid BultoId,
    Guid EnvioId,
    string Codigo,
    EstadoBulto EstadoBulto,
    EstadoEnvio EstadoEnvio,
    Guid? EventIdBulto,
    Guid? EventIdEnvio);

/// <summary>RF 22 a nivel bulto: rinde un bulto a Devuelto y deriva el estado del envío si corresponde.</summary>
public sealed class RendirBulto
{
    private readonly IBultoRepositorio _bultos;
    private readonly IEnvioRepositorio _envios;
    private readonly IOutboxRepositorio _outbox;
    private readonly RegistrarOutboxEstado _outboxEstado;
    private readonly PublicarOutboxPendientes _publicar;
    private readonly ICacheLectura _cache;

    public RendirBulto(
        IBultoRepositorio bultos,
        IEnvioRepositorio envios,
        IOutboxRepositorio outbox,
        RegistrarOutboxEstado outboxEstado,
        PublicarOutboxPendientes publicar,
        ICacheLectura cache)
    {
        _bultos = bultos;
        _envios = envios;
        _outbox = outbox;
        _outboxEstado = outboxEstado;
        _publicar = publicar;
        _cache = cache;
    }

    public Task<ResultadoRendicionBulto> PorCodigoAsync(
        Guid operadorId,
        string codigo,
        string usuario,
        string motivo = "Rendición en depósito",
        CancellationToken cancellationToken = default) =>
        EjecutarAsync(operadorId, codigo: codigo, bultoId: null, usuario, motivo, cancellationToken);

    public Task<ResultadoRendicionBulto> PorIdAsync(
        Guid operadorId,
        Guid bultoId,
        string usuario,
        string motivo = "Rendición en depósito",
        CancellationToken cancellationToken = default) =>
        EjecutarAsync(operadorId, codigo: null, bultoId, usuario, motivo, cancellationToken);

    private async Task<ResultadoRendicionBulto> EjecutarAsync(
        Guid operadorId,
        string? codigo,
        Guid? bultoId,
        string usuario,
        string motivo,
        CancellationToken cancellationToken)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));
        if (string.IsNullOrWhiteSpace(usuario))
            throw new ArgumentException("El usuario es requerido.", nameof(usuario));

        Bulto? bulto = null;
        if (bultoId is Guid id)
            bulto = await _bultos.ObtenerAsync(operadorId, id, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(codigo))
            bulto = await _bultos.ObtenerPorCodigoAsync(operadorId, codigo, cancellationToken);

        if (bulto is null)
            throw new InvalidOperationException("El bulto no existe para ese operador.");

        if (bulto.Estado == EstadoBulto.Devuelto)
            throw new InvalidOperationException($"El bulto {bulto.Codigo} ya fue rendido.");

        if (!TransicionesBulto.EsValida(bulto.Estado, EstadoBulto.Devuelto))
            throw new InvalidOperationException(
                $"El bulto {bulto.Codigo} está {bulto.Estado} y no se puede rendir.");

        var envio = await _envios.ObtenerPorOperadorAsync(bulto.EnvioId, operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El envío del bulto no existe.");

        var anteriorBulto = bulto.Estado;
        bulto.CambiarEstado(EstadoBulto.Devuelto, motivo);

        var eventoBulto = BultoEstadoCambiado.Crear(operadorId, envio.Id, bulto.Id, anteriorBulto, bulto.Estado);
        await _outbox.AgregarAsync(
            MensajeOutbox.CrearPendiente(
                eventoBulto.EventId,
                operadorId,
                envio.Id,
                BultoEstadoCambiado.Tipo,
                BultoEstadoCambiado.Version,
                eventoBulto.Serializar(),
                eventoBulto.Fecha,
                Activity.Current?.Id),
            cancellationToken);

        Guid? eventIdEnvio = null;
        var estadoEnvioAnterior = envio.Estado;
        var todos = await _bultos.ListarPorEnvioAsync(operadorId, envio.Id, cancellationToken);
        var estados = todos.Select(b => b.Id == bulto.Id ? bulto.Estado : b.Estado).ToList();

        if (envio.Estado == EstadoEnvio.EnDevolucion)
        {
            if (estados.All(e => e is EstadoBulto.Devuelto or EstadoBulto.Entregado))
            {
                envio.CambiarEstado(EstadoEnvio.Devuelto, usuario, motivo);
                eventIdEnvio = await _outboxEstado.EjecutarAsync(
                    operadorId, envio.Id, estadoEnvioAnterior, envio.Estado, cancellationToken,
                    motivo: motivo, responsable: usuario);
            }
        }
        else
        {
            var derivado = DerivacionEstadoEnvio.DesdeBultos(estados);
            if (derivado != envio.Estado && TransicionesEnvio.EsValida(envio.Estado, derivado))
            {
                envio.CambiarEstado(derivado, usuario, motivo);
                eventIdEnvio = await _outboxEstado.EjecutarAsync(
                    operadorId, envio.Id, estadoEnvioAnterior, envio.Estado, cancellationToken,
                    motivo: motivo, responsable: usuario);
            }
        }

        await _bultos.GuardarAsync(cancellationToken);
        await _publicar.EjecutarAsync(cancellationToken: cancellationToken);
        await CacheSeguimiento.PublicarAsync(_cache, envio, cancellationToken);

        return new ResultadoRendicionBulto(
            bulto.Id,
            envio.Id,
            bulto.Codigo,
            bulto.Estado,
            envio.Estado,
            eventoBulto.EventId,
            eventIdEnvio);
    }
}
