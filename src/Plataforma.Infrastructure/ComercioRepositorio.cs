using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class ComercioRepositorio : IComercioRepositorio
{
    private readonly PlataformaDbContext _context;

    public ComercioRepositorio(PlataformaDbContext context) => _context = context;

    public async Task AgregarAsync(Comercio comercio, CancellationToken cancellationToken = default) =>
        await _context.Comercios.AddAsync(comercio, cancellationToken);

    public Task<Comercio?> ObtenerAsync(Guid operadorId, Guid comercioId, CancellationToken cancellationToken = default) =>
        _context.Comercios.FirstOrDefaultAsync(
            c => c.OperadorId == operadorId && c.Id == comercioId, cancellationToken);

    public async Task<IReadOnlyList<Comercio>> ListarAsync(Guid operadorId, CancellationToken cancellationToken = default) =>
        await _context.Comercios.AsNoTracking()
            .Where(c => c.OperadorId == operadorId)
            .OrderBy(c => c.NombreComercial)
            .ToListAsync(cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
