using Plataforma.Domain;

namespace Plataforma.Application;

public interface IRecepcionRepositorio
{
    Task AgregarAsync(RecepcionEnvio recepcion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiscrepanciaRecepcion>> ListarDiscrepanciasAsync(
        Guid operadorId,
        int limite,
        CancellationToken cancellationToken = default);
    Task GuardarAsync(CancellationToken cancellationToken = default);
}

/// <summary>RF 10: recepción en depósito con registro de discrepancias de bultos.</summary>
public sealed class RecibirEnvioEnDeposito
{
    private readonly IEnvioRepositorio _envios;
    private readonly IBultoRepositorio _bultos;
    private readonly IRecepcionRepositorio _recepciones;
    private readonly CambiarEstadoEnvio _cambiarEstado;

    public RecibirEnvioEnDeposito(
        IEnvioRepositorio envios,
        IBultoRepositorio bultos,
        IRecepcionRepositorio recepciones,
        CambiarEstadoEnvio cambiarEstado)
    {
        _envios = envios;
        _bultos = bultos;
        _recepciones = recepciones;
        _cambiarEstado = cambiarEstado;
    }

    public async Task<ResultadoRecepcion> EjecutarAsync(
        Guid operadorId,
        Guid envioId,
        int bultosRecibidos,
        string usuario,
        string observaciones = "",
        CancellationToken cancellationToken = default)
    {
        var envio = await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El envío no existe para ese operador.");

        if (envio.Estado != EstadoEnvio.Admitido)
            throw new InvalidOperationException($"Solo se reciben envíos Admitido (actual: {envio.Estado}).");

        var bultos = await _bultos.ListarPorEnvioAsync(operadorId, envioId, cancellationToken);
        var esperados = Math.Max(1, bultos.Count);

        var recepcion = RecepcionEnvio.Registrar(
            Guid.NewGuid(),
            operadorId,
            envioId,
            esperados,
            bultosRecibidos,
            usuario,
            observaciones);

        await _recepciones.AgregarAsync(recepcion, cancellationToken);

        var motivo = recepcion.HayDiscrepancia
            ? $"Recepción con discrepancia: esperados {esperados}, recibidos {bultosRecibidos}. {observaciones}"
            : "Recepción en depósito";

        var transicion = await _cambiarEstado.EjecutarAsync(
            envioId,
            operadorId,
            EstadoEnvio.EnDeposito,
            usuario,
            motivo,
            cancellationToken);

        await _recepciones.GuardarAsync(cancellationToken);

        return new ResultadoRecepcion(
            envio.Id,
            recepcion.Id,
            recepcion.HayDiscrepancia,
            esperados,
            bultosRecibidos,
            transicion.EventId);
    }
}

public sealed record DiscrepanciaRecepcion(
    string NumeroSeguimiento,
    string Destinatario,
    int BultosEsperados,
    int BultosRecibidos,
    string Observaciones,
    string Usuario,
    DateTimeOffset Fecha);

public sealed record ResultadoRecepcion(
    Guid EnvioId,
    Guid RecepcionId,
    bool HayDiscrepancia,
    int BultosEsperados,
    int BultosRecibidos,
    Guid? EventId);
