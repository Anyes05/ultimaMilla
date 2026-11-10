using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

/// <summary>Asegura usuarios Identity alineados al seed de <see cref="UsuarioPlataforma"/>.</summary>
public static class SemillaIdentidad
{
    public static async Task SincronizarAsync(
        UserManager<UsuarioIdentity> users,
        PlataformaDbContext context,
        CancellationToken cancellationToken = default)
    {
        var perfiles = await context.Usuarios.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var perfil in perfiles)
        {
            if (await users.FindByIdAsync(perfil.Id.ToString()) is not null)
                continue;

            var identity = new UsuarioIdentity
            {
                Id = perfil.Id,
                UserName = perfil.NombreUsuario,
                NormalizedUserName = users.NormalizeName(perfil.NombreUsuario),
                Email = string.IsNullOrWhiteSpace(perfil.Email) ? $"{perfil.NombreUsuario}@local" : perfil.Email,
                NormalizedEmail = users.NormalizeEmail(
                    string.IsNullOrWhiteSpace(perfil.Email) ? $"{perfil.NombreUsuario}@local" : perfil.Email),
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("N")
            };

            // Misma clave de desarrollo del seed; en EKS el Secret puede diferir y el login
            // migrará con el hash legado la primera vez.
            var resultado = await users.CreateAsync(identity, DatosSemilla.ClaveDesarrollo);
            if (!resultado.Succeeded && resultado.Errors.All(e => e.Code != "DuplicateUserName"))
            {
                throw new InvalidOperationException(
                    $"No se pudo sembrar Identity para {perfil.NombreUsuario}: " +
                    string.Join("; ", resultado.Errors.Select(e => e.Description)));
            }
        }
    }
}
