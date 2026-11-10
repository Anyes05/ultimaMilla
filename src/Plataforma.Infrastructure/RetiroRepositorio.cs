using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class RetiroRepositorio : IRetiroRepositorio
{
    private readonly PlataformaDbContext _context;
    public RetiroRepositorio(PlataformaDbContext context) => _context = context;

    public async Task AgregarAsync(SolicitudRetiro retiro, CancellationToken cancellationToken = default) =>
        await _context.SolicitudesRetiro.AddAsync(retiro, cancellationToken);

    public Task<SolicitudRetiro?> ObtenerAsync(
        Guid operadorId,
        Guid retiroId,
        CancellationToken cancellationToken = default) =>
        _context.SolicitudesRetiro.FirstOrDefaultAsync(
            r => r.OperadorId == operadorId && r.Id == retiroId,
            cancellationToken);

    public async Task<IReadOnlyList<SolicitudRetiro>> ListarAsync(
        Guid operadorId,
        Guid? comercioId,
        CancellationToken cancellationToken = default)
    {
        var q = _context.SolicitudesRetiro.AsNoTracking().Where(r => r.OperadorId == operadorId);
        if (comercioId is Guid c)
            q = q.Where(r => r.ComercioId == c);
        return await q.OrderByDescending(r => r.CreadoEn).ToListAsync(cancellationToken);
    }

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
