using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class CierreDiarioRepositorio : ICierreDiarioRepositorio
{
    private readonly PlataformaDbContext _context;

    public CierreDiarioRepositorio(PlataformaDbContext context) => _context = context;

    public async Task<IReadOnlyList<CierreDiario>> ListarAsync(
        Guid operadorId,
        DateOnly desde,
        DateOnly hasta,
        CancellationToken cancellationToken = default) =>
        await _context.CierresDiarios.AsNoTracking()
            .Where(cierre => cierre.OperadorId == operadorId && cierre.Dia >= desde && cierre.Dia <= hasta)
            .OrderByDescending(cierre => cierre.Dia)
            .ToListAsync(cancellationToken);
}
