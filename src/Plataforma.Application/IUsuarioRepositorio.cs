using Plataforma.Domain;

namespace Plataforma.Application;

public interface IUsuarioRepositorio
{
    Task<UsuarioPlataforma?> BuscarPorNombreAsync(string nombreUsuario, CancellationToken cancellationToken = default);
}
