using Plataforma.Domain;

namespace Plataforma.Application;

public interface IRepartidorRepositorio
{
    Task<Repartidor?> ObtenerAsync(Guid repartidorId, Guid operadorId, CancellationToken cancellationToken = default);

    Task<Repartidor?> ObtenerPorUsuarioAsync(Guid usuarioId, Guid operadorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Repartidor>> ListarActivosAsync(Guid operadorId, CancellationToken cancellationToken = default);
}
