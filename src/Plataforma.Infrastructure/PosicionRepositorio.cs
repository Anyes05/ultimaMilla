using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class PosicionRepositorio : IPosicionRepositorio
{
    private readonly PlataformaDbContext _context;

    public PosicionRepositorio(PlataformaDbContext context) => _context = context;

    public Task AgregarAsync(IEnumerable<PosicionRepartidor> posiciones, CancellationToken cancellationToken = default) =>
        _context.PosicionesRepartidor.AddRangeAsync(posiciones, cancellationToken);

    public async Task<IReadOnlyList<PosicionRepartidor>> UltimasPorRepartidorAsync(
        Guid operadorId,
        DateTime desdeUtc,
        CancellationToken cancellationToken = default)
    {
        var desde = DateTime.SpecifyKind(desdeUtc, DateTimeKind.Utc);
        // Primero la hora más reciente de cada repartidor (lo resuelve la base con el índice), después esas filas.
        var maximas = await _context.PosicionesRepartidor.AsNoTracking()
            .Where(posicion => posicion.OperadorId == operadorId && posicion.FechaUtc >= desde)
            .GroupBy(posicion => posicion.RepartidorId)
            .Select(grupo => new { RepartidorId = grupo.Key, Fecha = grupo.Max(posicion => posicion.FechaUtc) })
            .ToListAsync(cancellationToken);
        if (maximas.Count == 0)
            return [];

        var repartidores = maximas.Select(maxima => maxima.RepartidorId).ToList();
        var candidatas = await _context.PosicionesRepartidor.AsNoTracking()
            .Where(posicion => posicion.OperadorId == operadorId
                               && repartidores.Contains(posicion.RepartidorId)
                               && posicion.FechaUtc >= desde)
            .Where(posicion => maximas.Select(maxima => maxima.Fecha).Contains(posicion.FechaUtc))
            .ToListAsync(cancellationToken);

        return maximas
            .Select(maxima => candidatas
                .Where(posicion => posicion.RepartidorId == maxima.RepartidorId && posicion.FechaUtc == maxima.Fecha)
                .OrderByDescending(posicion => posicion.RecibidaUtc)
                .FirstOrDefault())
            .OfType<PosicionRepartidor>()
            .ToList();
    }

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
