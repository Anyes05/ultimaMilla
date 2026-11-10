using Plataforma.Domain;

namespace Plataforma.Application;

public interface IBanderasOperador
{
    Task<bool> EstaActivaAsync(Guid operadorId, string clave, CancellationToken cancellationToken = default);
}

public interface IBanderaRepositorio
{
    Task<BanderaOperador?> ObtenerAsync(Guid operadorId, string clave, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BanderaOperador>> ListarPorOperadorAsync(Guid operadorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BanderaOperador>> ListarTodasAsync(CancellationToken cancellationToken = default);
    Task AgregarAsync(BanderaOperador bandera, CancellationToken cancellationToken = default);
    Task AgregarCambioAsync(CambioBandera cambio, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CambioBandera>> HistorialAsync(Guid? operadorId, CancellationToken cancellationToken = default);
    Task GuardarAsync(CancellationToken cancellationToken = default);
}
