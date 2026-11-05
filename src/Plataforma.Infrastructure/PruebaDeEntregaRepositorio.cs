using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class PruebaDeEntregaRepositorio : IPruebaDeEntregaRepositorio
{
    private readonly PlataformaDbContext _context;

    public PruebaDeEntregaRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public async Task AgregarAsync(PruebaDeEntrega prueba, CancellationToken cancellationToken = default)
    {
        await _context.PruebasDeEntrega.AddAsync(prueba, cancellationToken);
    }

    public Task<List<PruebaDeEntrega>> ListarPorEnvioAsync(Guid operadorId, Guid envioId, CancellationToken cancellationToken = default) =>
        _context.PruebasDeEntrega.AsNoTracking()
            .Where(prueba => prueba.OperadorId == operadorId && prueba.EnvioId == envioId)
            .OrderBy(prueba => prueba.Fecha)
            .ToListAsync(cancellationToken);
}
