using Plataforma.Domain;

namespace Plataforma.Application;

public interface IDispositivoRepositorio
{
    Task<DispositivoRepartidor?> ObtenerPorTokenAsync(
        Guid operadorId,
        Guid repartidorId,
        string token,
        CancellationToken cancellationToken = default);

    Task AgregarAsync(DispositivoRepartidor dispositivo, CancellationToken cancellationToken = default);

    Task QuitarAsync(DispositivoRepartidor dispositivo, CancellationToken cancellationToken = default);

    Task GuardarAsync(CancellationToken cancellationToken = default);
}
