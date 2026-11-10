using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class RecepcionRepositorio : IRecepcionRepositorio
{
    private readonly PlataformaDbContext _context;
    public RecepcionRepositorio(PlataformaDbContext context) => _context = context;

    public async Task AgregarAsync(RecepcionEnvio recepcion, CancellationToken cancellationToken = default) =>
        await _context.RecepcionesEnvio.AddAsync(recepcion, cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
