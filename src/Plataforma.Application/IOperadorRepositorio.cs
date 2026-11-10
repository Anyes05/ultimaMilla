using Plataforma.Domain;

namespace Plataforma.Application;

public interface IOperadorRepositorio
{
    Task<Operador?> ObtenerAsync(Guid operadorId, CancellationToken cancellationToken = default);

    Task<Operador?> ObtenerParaEditarAsync(Guid operadorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> ListarIdsAsync(CancellationToken cancellationToken = default);
}
