using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class DispositivoRepositorio : IDispositivoRepositorio
{
    private readonly PlataformaDbContext _context;

    public DispositivoRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public Task<DispositivoRepartidor?> ObtenerPorTokenAsync(
        Guid operadorId,
        Guid repartidorId,
        string token,
        CancellationToken cancellationToken = default) =>
        _context.DispositivosRepartidor.FirstOrDefaultAsync(
            dispositivo =>
                dispositivo.OperadorId == operadorId &&
                dispositivo.RepartidorId == repartidorId &&
                dispositivo.Token == token,
            cancellationToken);

    public async Task AgregarAsync(DispositivoRepartidor dispositivo, CancellationToken cancellationToken = default) =>
        await _context.DispositivosRepartidor.AddAsync(dispositivo, cancellationToken);

    public Task QuitarAsync(DispositivoRepartidor dispositivo, CancellationToken cancellationToken = default)
    {
        _context.DispositivosRepartidor.Remove(dispositivo);
        return Task.CompletedTask;
    }

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
