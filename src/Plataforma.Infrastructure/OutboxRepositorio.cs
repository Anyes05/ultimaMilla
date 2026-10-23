using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class OutboxRepositorio : IOutboxRepositorio
{
    private readonly PlataformaDbContext _context;

    public OutboxRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public async Task AgregarAsync(MensajeOutbox mensaje, CancellationToken cancellationToken = default) =>
        await _context.MensajesOutbox.AddAsync(mensaje, cancellationToken);

    public Task<bool> ExisteAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        _context.MensajesOutbox.AnyAsync(mensaje => mensaje.EventId == eventId, cancellationToken);

    public async Task<IReadOnlyList<MensajeOutbox>> ListarPendientesAsync(
        int maximo,
        CancellationToken cancellationToken = default) =>
        await _context.MensajesOutbox
            .Where(mensaje => mensaje.PublicadoEn == null)
            .OrderBy(mensaje => mensaje.Fecha)
            .Take(maximo < 1 ? 50 : maximo)
            .ToListAsync(cancellationToken);

    public Task<MensajeOutbox?> ObtenerAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        _context.MensajesOutbox.FirstOrDefaultAsync(mensaje => mensaje.EventId == eventId, cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
