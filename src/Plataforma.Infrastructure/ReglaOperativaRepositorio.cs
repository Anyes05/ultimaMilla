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
        _context.ReglasOperativas.AsNoTracking()
            .Where(regla => regla.OperadorId == operadorId
                && regla.Activa
                && regla.VigenciaDesde <= instanteUtc
                && instanteUtc <= regla.VigenciaHasta)
            .OrderByDescending(regla => regla.VigenciaDesde)
            .FirstOrDefaultAsync(cancellationToken);
}
