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

    public Task<Ruta?> ObtenerAsync(Guid operadorId, Guid rutaId, CancellationToken cancellationToken = default) =>
        _context.Rutas.FirstOrDefaultAsync(
            ruta => ruta.OperadorId == operadorId && ruta.Id == rutaId,
            cancellationToken);

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

    public Task<Parada?> ObtenerParadaPorEnvioAsync(
        Guid operadorId,
        Guid envioId,
        CancellationToken cancellationToken = default) =>
        _context.Paradas.FirstOrDefaultAsync(
            parada => parada.OperadorId == operadorId && parada.EnvioId == envioId,
            cancellationToken);

    public Task QuitarParadaAsync(Parada parada, CancellationToken cancellationToken = default)
    {
        _context.Paradas.Remove(parada);
        return Task.CompletedTask;
    }

    public async Task AgregarParadaAsync(Parada parada, CancellationToken cancellationToken = default) =>
        await _context.Paradas.AddAsync(parada, cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
