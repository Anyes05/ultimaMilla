using Plataforma.Domain;

namespace Plataforma.Application;

public interface IBultoRepositorio
{
    Task AgregarAsync(Bulto bulto, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Bulto>> ListarPorEnvioAsync(
        Guid operadorId,
        Guid envioId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Bulto>> ListarDisponiblesParaNuevaRutaAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default);
}
