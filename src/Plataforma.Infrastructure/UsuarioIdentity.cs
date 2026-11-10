using Microsoft.AspNetCore.Identity;

namespace Plataforma.Infrastructure;

/// <summary>
/// Usuario de ASP.NET Identity (letra 6.3 / Opción A). El Id coincide con <c>UsuarioPlataforma.Id</c>.
/// El perfil de negocio (rol, operador, comercio) sigue en <c>UsuarioPlataforma</c>.
/// </summary>
public sealed class UsuarioIdentity : IdentityUser<Guid>
{
}
