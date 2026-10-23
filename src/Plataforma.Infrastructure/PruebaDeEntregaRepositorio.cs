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
}
