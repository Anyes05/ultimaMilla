using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class LiquidacionRepositorio : ILiquidacionRepositorio
{
    private readonly PlataformaDbContext _context;

    public LiquidacionRepositorio(PlataformaDbContext context) => _context = context;

    public async Task AgregarAsync(Liquidacion liquidacion, CancellationToken cancellationToken = default) =>
        await _context.Liquidaciones.AddAsync(liquidacion, cancellationToken);

    public Task<Liquidacion?> ObtenerAsync(
        Guid operadorId,
        Guid liquidacionId,
        CancellationToken cancellationToken = default) =>
        _context.Liquidaciones.FirstOrDefaultAsync(
            l => l.OperadorId == operadorId && l.Id == liquidacionId,
            cancellationToken);

    public async Task<IReadOnlyList<Liquidacion>> ListarAsync(
        Guid operadorId,
        Guid? comercioId = null,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Liquidaciones.AsNoTracking().Where(l => l.OperadorId == operadorId);
        if (comercioId is Guid c)
            q = q.Where(l => l.ComercioId == c);
        return await q.OrderByDescending(l => l.PeriodoDesde).ToListAsync(cancellationToken);
    }

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
