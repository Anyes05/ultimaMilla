using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class CoberturaRepositorio : ICoberturaRepositorio
{
    private readonly PlataformaDbContext _context;
    public CoberturaRepositorio(PlataformaDbContext context) => _context = context;

    public async Task AgregarZonaAsync(ZonaCobertura zona, CancellationToken cancellationToken = default) =>
        await _context.ZonasCobertura.AddAsync(zona, cancellationToken);

    public async Task AgregarFranjaAsync(FranjaHoraria franja, CancellationToken cancellationToken = default) =>
        await _context.FranjasHorarias.AddAsync(franja, cancellationToken);

    public async Task AgregarVehiculoAsync(Vehiculo vehiculo, CancellationToken cancellationToken = default) =>
        await _context.Vehiculos.AddAsync(vehiculo, cancellationToken);

    public Task<ZonaCobertura?> ObtenerZonaAsync(
        Guid operadorId,
        Guid zonaId,
        CancellationToken cancellationToken = default) =>
        _context.ZonasCobertura.FirstOrDefaultAsync(
            z => z.OperadorId == operadorId && z.Id == zonaId, cancellationToken);

    public Task<FranjaHoraria?> ObtenerFranjaAsync(
        Guid operadorId,
        Guid franjaId,
        CancellationToken cancellationToken = default) =>
        _context.FranjasHorarias.FirstOrDefaultAsync(
            f => f.OperadorId == operadorId && f.Id == franjaId, cancellationToken);

    public async Task<IReadOnlyList<FranjaHoraria>> ListarTodasLasFranjasAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        await _context.FranjasHorarias
            .Where(franja => franja.OperadorId == operadorId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Vehiculo>> ListarTodosLosVehiculosAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        await _context.Vehiculos
            .Where(vehiculo => vehiculo.OperadorId == operadorId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ZonaCobertura>> ListarZonasAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        await _context.ZonasCobertura.AsNoTracking()
            .Where(z => z.OperadorId == operadorId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FranjaHoraria>> ListarFranjasAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        await _context.FranjasHorarias.AsNoTracking()
            .Where(f => f.OperadorId == operadorId && f.Activa)
            .OrderBy(f => f.Desde)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Vehiculo>> ListarVehiculosAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        await _context.Vehiculos.AsNoTracking()
            .Where(v => v.OperadorId == operadorId && v.Activo)
            .ToListAsync(cancellationToken);

    public Task<Vehiculo?> ObtenerVehiculoAsync(
        Guid operadorId,
        Guid vehiculoId,
        CancellationToken cancellationToken = default) =>
        _context.Vehiculos.FirstOrDefaultAsync(
            v => v.OperadorId == operadorId && v.Id == vehiculoId,
            cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
