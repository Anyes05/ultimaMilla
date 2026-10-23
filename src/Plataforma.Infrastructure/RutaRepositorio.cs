using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class RutaRepositorio : IRutaRepositorio
{
    private readonly PlataformaDbContext _context;

    public RutaRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Ruta>> ObtenerDeHoyAsync(
        Guid operadorId,
        Guid repartidorId,
        DateOnly fecha,
        CancellationToken cancellationToken = default) =>
        await _context.Rutas.AsNoTracking()
            .Where(ruta => ruta.OperadorId == operadorId && ruta.RepartidorId == repartidorId && ruta.Fecha == fecha)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Ruta>> ObtenerDelDiaAsync(
        Guid operadorId,
        DateOnly fecha,
        CancellationToken cancellationToken = default) =>
        await _context.Rutas.AsNoTracking()
            .Where(ruta => ruta.OperadorId == operadorId && ruta.Fecha == fecha)
            .OrderBy(ruta => ruta.RepartidorId)
            .ToListAsync(cancellationToken);

    public async Task GuardarDespachoAsync(
        Ruta ruta,
        IReadOnlyList<Parada> paradas,
        CancellationToken cancellationToken = default)
    {
        await _context.Rutas.AddAsync(ruta, cancellationToken);
        await _context.Paradas.AddRangeAsync(paradas, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
