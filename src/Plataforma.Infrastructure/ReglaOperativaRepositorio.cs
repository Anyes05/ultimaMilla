using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class ReglaOperativaRepositorio : IReglaOperativaRepositorio
{
    private readonly PlataformaDbContext _context;

    public ReglaOperativaRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public Task<ReglaOperativa?> ObtenerVigenteAsync(
        Guid operadorId,
        DateTime instanteUtc,
        CancellationToken cancellationToken = default) =>
        Vigentes(_context.ReglasOperativas.AsNoTracking(), operadorId, instanteUtc).FirstOrDefaultAsync(cancellationToken);

    public Task<ReglaOperativa?> ObtenerVigenteParaEditarAsync(
        Guid operadorId,
        DateTime instanteUtc,
        CancellationToken cancellationToken = default) =>
        Vigentes(_context.ReglasOperativas, operadorId, instanteUtc).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ReglaOperativa>> ListarVersionesAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        await _context.ReglasOperativas.AsNoTracking()
            .Where(regla => regla.OperadorId == operadorId)
            .OrderByDescending(regla => regla.VigenciaDesde)
            .ToListAsync(cancellationToken);

    public async Task AgregarAsync(ReglaOperativa regla, CancellationToken cancellationToken = default) =>
        await _context.ReglasOperativas.AddAsync(regla, cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    private static IQueryable<ReglaOperativa> Vigentes(IQueryable<ReglaOperativa> reglas, Guid operadorId, DateTime instanteUtc) =>
        reglas.Where(regla => regla.OperadorId == operadorId
                && regla.Activa
                && regla.VigenciaDesde <= instanteUtc
                && instanteUtc <= regla.VigenciaHasta)
            .OrderByDescending(regla => regla.VigenciaDesde);
}
