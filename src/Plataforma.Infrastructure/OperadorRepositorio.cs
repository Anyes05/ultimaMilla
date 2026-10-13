using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class OperadorRepositorio : IOperadorRepositorio
{
    private readonly PlataformaDbContext _context;

    public OperadorRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public Task<Operador?> ObtenerAsync(Guid operadorId, CancellationToken cancellationToken = default) =>
        _context.Operadores.AsNoTracking().FirstOrDefaultAsync(operador => operador.Id == operadorId, cancellationToken);
}
