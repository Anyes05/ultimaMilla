using Plataforma.Domain;

namespace Plataforma.Application;

public interface ICierreDiarioRepositorio
{
    Task<IReadOnlyList<CierreDiario>> ListarAsync(
        Guid operadorId,
        DateOnly desde,
        DateOnly hasta,
        CancellationToken cancellationToken = default);
}

/// <summary>Cierres diarios que deja el worker, para mostrarlos en Reportes.</summary>
public sealed class ListarCierresDiarios
{
    private readonly ICierreDiarioRepositorio _cierres;

    public ListarCierresDiarios(ICierreDiarioRepositorio cierres) => _cierres = cierres;

    public Task<IReadOnlyList<CierreDiario>> EjecutarAsync(
        Guid operadorId,
        DateOnly desde,
        DateOnly hasta,
        CancellationToken cancellationToken = default) =>
        _cierres.ListarAsync(operadorId, desde, hasta, cancellationToken);
}
