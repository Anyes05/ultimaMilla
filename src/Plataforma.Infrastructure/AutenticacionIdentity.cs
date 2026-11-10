using Microsoft.AspNetCore.Identity;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

public sealed class AutenticacionIdentity : IAutenticacionIdentity
{
    private readonly UserManager<UsuarioIdentity> _users;
    private readonly IVerificadorClave _legado;

    public AutenticacionIdentity(UserManager<UsuarioIdentity> users, IVerificadorClave legado)
    {
        _users = users;
        _legado = legado;
    }

    public async Task<bool> VerificarAsync(
        Guid usuarioId,
        string nombreUsuario,
        string email,
        string clave,
        string hashLegado,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var identity = await _users.FindByIdAsync(usuarioId.ToString())
            ?? await _users.FindByNameAsync(nombreUsuario);

        if (identity is not null)
        {
            if (await _users.CheckPasswordAsync(identity, clave))
                return true;

            // Si el seed Identity usó Demo123! pero el perfil aún tiene otra clave (EKS),
            // realineamos Identity al hash legado válido.
            if (_legado.Coincide(clave, hashLegado))
            {
                await _users.RemovePasswordAsync(identity);
                var reset = await _users.AddPasswordAsync(identity, clave);
                return reset.Succeeded;
            }

            return false;
        }

        // Primera vez / pre-migración: valida hash legado y crea el usuario Identity.
        if (!_legado.Coincide(clave, hashLegado))
            return false;

        identity = new UsuarioIdentity
        {
            Id = usuarioId,
            UserName = nombreUsuario,
            NormalizedUserName = _users.NormalizeName(nombreUsuario),
            Email = string.IsNullOrWhiteSpace(email) ? $"{nombreUsuario}@local" : email,
            NormalizedEmail = _users.NormalizeEmail(
                string.IsNullOrWhiteSpace(email) ? $"{nombreUsuario}@local" : email),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        var creado = await _users.CreateAsync(identity, clave);
        return creado.Succeeded;
    }

    public async Task CrearAsync(
        Guid usuarioId,
        string nombreUsuario,
        string email,
        string clave,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (await _users.FindByIdAsync(usuarioId.ToString()) is not null)
            return;

        var identity = new UsuarioIdentity
        {
            Id = usuarioId,
            UserName = nombreUsuario,
            NormalizedUserName = _users.NormalizeName(nombreUsuario),
            Email = string.IsNullOrWhiteSpace(email) ? $"{nombreUsuario}@local" : email,
            NormalizedEmail = _users.NormalizeEmail(
                string.IsNullOrWhiteSpace(email) ? $"{nombreUsuario}@local" : email),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        var creado = await _users.CreateAsync(identity, clave);
        if (!creado.Succeeded)
            throw new InvalidOperationException(string.Join("; ", creado.Errors.Select(e => e.Description)));
    }
}
