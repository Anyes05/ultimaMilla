using Plataforma.Domain;

namespace Plataforma.Application;

public interface IUsuarioRepositorio
{
    Task<UsuarioPlataforma?> BuscarPorNombreAsync(string nombreUsuario, CancellationToken cancellationToken = default);
    Task<UsuarioPlataforma?> ObtenerAsync(Guid operadorId, Guid usuarioId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsuarioPlataforma>> ListarAsync(Guid operadorId, CancellationToken cancellationToken = default);
    Task AgregarAsync(UsuarioPlataforma usuario, CancellationToken cancellationToken = default);
    Task GuardarAsync(CancellationToken cancellationToken = default);
}
