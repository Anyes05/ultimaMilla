using Plataforma.Domain;

namespace Plataforma.Application;

public interface IOperadorRepositorio
{
    Task<Operador?> ObtenerAsync(Guid operadorId, CancellationToken cancellationToken = default);
}
