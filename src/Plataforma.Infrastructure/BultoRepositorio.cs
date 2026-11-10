using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class BultoRepositorio : IBultoRepositorio
{
    private readonly PlataformaDbContext _context;

    public BultoRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public Task AgregarAsync(Bulto bulto, CancellationToken cancellationToken = default)
    {
        _context.Bultos.Add(bulto);
        return Task.CompletedTask;
    }

    public Task<Bulto?> ObtenerAsync(Guid operadorId, Guid bultoId, CancellationToken cancellationToken = default) =>
        _context.Bultos.FirstOrDefaultAsync(
            b => b.OperadorId == operadorId && b.Id == bultoId,
            cancellationToken);

    public Task<Bulto?> ObtenerPorCodigoAsync(
        Guid operadorId,
        string codigo,
        CancellationToken cancellationToken = default)
    {
        var normalizado = codigo.Trim();
        return _context.Bultos.FirstOrDefaultAsync(
            b => b.OperadorId == operadorId && b.Codigo == normalizado,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Bulto>> ListarPorEnvioAsync(
        Guid operadorId,
        Guid envioId,
        CancellationToken cancellationToken = default) =>
        await _context.Bultos
            .Where(bulto => bulto.OperadorId == operadorId && bulto.EnvioId == envioId)
            .OrderBy(bulto => bulto.Codigo)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Bulto>> ListarDisponiblesParaNuevaRutaAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        await _context.Bultos
            .Where(bulto =>
                bulto.OperadorId == operadorId &&
                (bulto.Estado == EstadoBulto.Pendiente || bulto.Estado == EstadoBulto.PendienteNuevaVisita))
            .OrderBy(bulto => bulto.Codigo)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Bulto>> ListarPendientesRendicionAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        await _context.Bultos
            .Where(bulto =>
                bulto.OperadorId == operadorId &&
                (bulto.Estado == EstadoBulto.Pendiente
                 || bulto.Estado == EstadoBulto.NoEntregado
                 || bulto.Estado == EstadoBulto.PendienteNuevaVisita))
            .OrderBy(bulto => bulto.Codigo)
            .ToListAsync(cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
