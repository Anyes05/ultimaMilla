using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class OperadorRepositorio : IOperadorRepositorio
{
    private readonly PlataformaDbContext _context;

    public OperadorRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public Task<Operador?> ObtenerAsync(Guid operadorId, CancellationToken cancellationToken = default) =>
        _context.Operadores.AsNoTracking().FirstOrDefaultAsync(operador => operador.Id == operadorId, cancellationToken);

    public Task<Operador?> ObtenerParaEditarAsync(Guid operadorId, CancellationToken cancellationToken = default) =>
        _context.Operadores.FirstOrDefaultAsync(operador => operador.Id == operadorId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListarIdsAsync(CancellationToken cancellationToken = default) =>
        await _context.Operadores.IgnoreQueryFilters().AsNoTracking()
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
