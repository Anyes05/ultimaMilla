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
}
