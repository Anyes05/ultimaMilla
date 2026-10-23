using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class EventoSincronizacionRepositorio : IEventoSincronizacionRepositorio
{
    private readonly PlataformaDbContext _context;

    public EventoSincronizacionRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public Task<EventoSincronizacionProcesado?> ObtenerAsync(
        Guid eventoId,
        CancellationToken cancellationToken = default) =>
        _context.EventosSincronizacionProcesados.FirstOrDefaultAsync(
            evento => evento.EventoId == eventoId,
            cancellationToken);

    public async Task AgregarAsync(
        EventoSincronizacionProcesado evento,
        CancellationToken cancellationToken = default) =>
        await _context.EventosSincronizacionProcesados.AddAsync(evento, cancellationToken);
}
