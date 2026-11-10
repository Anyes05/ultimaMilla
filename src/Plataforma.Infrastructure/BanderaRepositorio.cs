using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class BanderaRepositorio : IBanderaRepositorio
{
    private readonly PlataformaDbContext _context;

    public BanderaRepositorio(PlataformaDbContext context) => _context = context;

    public Task<BanderaOperador?> ObtenerAsync(
        Guid operadorId,
        string clave,
        CancellationToken cancellationToken = default) =>
        _context.BanderasOperador.FirstOrDefaultAsync(
            b => b.OperadorId == operadorId && b.Clave == clave,
            cancellationToken);

    public async Task<IReadOnlyList<BanderaOperador>> ListarPorOperadorAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        await _context.BanderasOperador.AsNoTracking()
            .Where(b => b.OperadorId == operadorId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BanderaOperador>> ListarTodasAsync(
        CancellationToken cancellationToken = default) =>
        await _context.BanderasOperador.IgnoreQueryFilters().AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task AgregarAsync(BanderaOperador bandera, CancellationToken cancellationToken = default) =>
        await _context.BanderasOperador.AddAsync(bandera, cancellationToken);

    public async Task AgregarCambioAsync(CambioBandera cambio, CancellationToken cancellationToken = default) =>
        await _context.CambiosBandera.AddAsync(cambio, cancellationToken);

    public async Task<IReadOnlyList<CambioBandera>> HistorialAsync(
        Guid? operadorId,
        CancellationToken cancellationToken = default)
    {
        var consulta = _context.CambiosBandera.IgnoreQueryFilters().AsNoTracking().AsQueryable();
        if (operadorId is Guid id)
            consulta = consulta.Where(c => c.OperadorId == id);
        return await consulta.OrderByDescending(c => c.Fecha).Take(200).ToListAsync(cancellationToken);
    }

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
