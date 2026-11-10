using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class HistorialEstadoRepositorio : IHistorialEstadoRepositorio
{
    private readonly PlataformaDbContext _context;

    public HistorialEstadoRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public async Task AgregarAsync(HistorialEstado historial, CancellationToken cancellationToken = default) =>
        await _context.HistorialEstados.AddAsync(historial, cancellationToken);

    public async Task<IReadOnlyList<HistorialEstado>> ListarPorEnvioAsync(
        Guid operadorId,
        Guid envioId,
        CancellationToken cancellationToken = default) =>
        await _context.HistorialEstados.AsNoTracking()
            .Where(historial => historial.OperadorId == operadorId && historial.EnvioId == envioId)
            .OrderBy(historial => historial.Fecha)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<HistorialEstado>> ListarPorEnviosAsync(
        Guid operadorId,
        IReadOnlyCollection<Guid> envioIds,
        CancellationToken cancellationToken = default)
    {
        if (envioIds.Count == 0)
            return [];

        var ids = envioIds.ToList();
        return await _context.HistorialEstados.AsNoTracking()
            .Where(historial => historial.OperadorId == operadorId && ids.Contains(historial.EnvioId))
            .OrderBy(historial => historial.Fecha)
            .ToListAsync(cancellationToken);
    }
}
