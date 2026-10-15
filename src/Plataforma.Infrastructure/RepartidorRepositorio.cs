using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class RepartidorRepositorio : IRepartidorRepositorio
{
    private readonly PlataformaDbContext _context;

    public RepartidorRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public Task<Repartidor?> ObtenerAsync(Guid repartidorId, Guid operadorId, CancellationToken cancellationToken = default) =>
        _context.Repartidores.AsNoTracking().FirstOrDefaultAsync(
            repartidor => repartidor.Id == repartidorId && repartidor.OperadorId == operadorId,
            cancellationToken);

    public Task<Repartidor?> ObtenerPorUsuarioAsync(Guid usuarioId, Guid operadorId, CancellationToken cancellationToken = default) =>
        _context.Repartidores.AsNoTracking().FirstOrDefaultAsync(
            repartidor => repartidor.UsuarioId == usuarioId && repartidor.OperadorId == operadorId,
            cancellationToken);
}
