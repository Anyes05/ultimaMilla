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

    public Task<UsuarioPlataforma?> ObtenerAsync(Guid operadorId, Guid usuarioId, CancellationToken cancellationToken = default) =>
        _context.Usuarios.FirstOrDefaultAsync(
            u => u.OperadorId == operadorId && u.Id == usuarioId,
            cancellationToken);

    public async Task<IReadOnlyList<UsuarioPlataforma>> ListarAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        await _context.Usuarios.AsNoTracking()
            .Where(u => u.OperadorId == operadorId)
            .OrderBy(u => u.NombreUsuario)
            .ToListAsync(cancellationToken);

    public async Task AgregarAsync(UsuarioPlataforma usuario, CancellationToken cancellationToken = default) =>
        await _context.Usuarios.AddAsync(usuario, cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
