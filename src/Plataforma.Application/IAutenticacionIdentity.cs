namespace Plataforma.Application;

/// <summary>
/// Puente a ASP.NET Identity (Opción A). Verifica la clave con Identity y, si hace falta,
/// migra desde el hash legado <c>pbkdf2$</c> de <c>UsuarioPlataforma</c>.
/// </summary>
public interface IAutenticacionIdentity
{
    Task<bool> VerificarAsync(
        Guid usuarioId,
        string nombreUsuario,
        string email,
        string clave,
        string hashLegado,
        CancellationToken cancellationToken = default);
}
