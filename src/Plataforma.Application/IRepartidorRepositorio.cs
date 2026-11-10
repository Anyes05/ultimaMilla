using Plataforma.Domain;

namespace Plataforma.Application;

public interface IRepartidorRepositorio
{
    Task<Repartidor?> ObtenerAsync(Guid repartidorId, Guid operadorId, CancellationToken cancellationToken = default);

    Task<Repartidor?> ObtenerParaEditarAsync(Guid repartidorId, Guid operadorId, CancellationToken cancellationToken = default);

    Task<Repartidor?> ObtenerPorUsuarioAsync(Guid usuarioId, Guid operadorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Repartidor>> ListarActivosAsync(Guid operadorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Repartidor>> ListarAsync(Guid operadorId, CancellationToken cancellationToken = default);

    Task AgregarAsync(Repartidor repartidor, CancellationToken cancellationToken = default);

    Task GuardarAsync(CancellationToken cancellationToken = default);
}
