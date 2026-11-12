using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class ComprobanteRetiroRepositorio : IComprobanteRetiroRepositorio
{
    private readonly PlataformaDbContext _context;

    public ComprobanteRetiroRepositorio(PlataformaDbContext context) => _context = context;

    public Task<ComprobanteRetiro?> ObtenerPorEventoAsync(Guid operadorId, Guid eventoId, CancellationToken cancellationToken = default) =>
        _context.ComprobantesRetiro.AsNoTracking()
            .FirstOrDefaultAsync(comprobante => comprobante.OperadorId == operadorId && comprobante.EventoId == eventoId, cancellationToken);

    public async Task<IReadOnlyList<ComprobanteRetiro>> ListarPorRetirosAsync(
        Guid operadorId,
        IReadOnlyCollection<Guid> retiroIds,
        CancellationToken cancellationToken = default)
    {
        if (retiroIds.Count == 0)
            return [];
        return await _context.ComprobantesRetiro.AsNoTracking()
            .Where(comprobante => comprobante.OperadorId == operadorId && retiroIds.Contains(comprobante.RetiroId))
            .OrderBy(comprobante => comprobante.RegistradoUtc)
            .ToListAsync(cancellationToken);
    }

    public void Agregar(ComprobanteRetiro comprobante) => _context.ComprobantesRetiro.Add(comprobante);

    public void Descartar(ComprobanteRetiro comprobante) => _context.Entry(comprobante).State = EntityState.Detached;
}
