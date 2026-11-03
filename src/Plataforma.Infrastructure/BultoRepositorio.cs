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
}
