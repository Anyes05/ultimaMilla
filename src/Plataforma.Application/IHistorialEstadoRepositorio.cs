using Plataforma.Domain;

namespace Plataforma.Application;

public interface IHistorialEstadoRepositorio
{
    Task AgregarAsync(HistorialEstado historial, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HistorialEstado>> ListarPorEnvioAsync(
        Guid operadorId,
        Guid envioId,
        CancellationToken cancellationToken = default);

    /// <summary>Historial de varios envíos en una sola consulta (reportes y tablero).</summary>
    Task<IReadOnlyList<HistorialEstado>> ListarPorEnviosAsync(
        Guid operadorId,
        IReadOnlyCollection<Guid> envioIds,
        CancellationToken cancellationToken = default);
}
