using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class UsuarioRepositorio : IUsuarioRepositorio
{
    private readonly PlataformaDbContext _context;

    public UsuarioRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public Task<UsuarioPlataforma?> BuscarPorNombreAsync(string nombreUsuario, CancellationToken cancellationToken = default)
    {
        var nombre = nombreUsuario.Trim().ToLowerInvariant();
        return _context.Usuarios.FirstOrDefaultAsync(usuario => usuario.NombreUsuario == nombre, cancellationToken);
    }
}
