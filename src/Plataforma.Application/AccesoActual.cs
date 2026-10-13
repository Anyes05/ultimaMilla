using System.Security.Claims;

namespace Plataforma.Application;

public sealed record AccesoActual(Guid UsuarioId, Guid OperadorId, Guid? ComercioId, string Rol)
{
    public static bool Intentar(ClaimsPrincipal principal, out AccesoActual acceso)
    {
        acceso = null!;
        var operador = principal.FindFirst(ClaimsPlataforma.OperadorId)?.Value;
        var rol = principal.FindFirst(ClaimsPlataforma.Rol)?.Value;
        var usuario = principal.FindFirst(ClaimsPlataforma.UsuarioId)?.Value;

        if (!Guid.TryParse(operador, out var operadorId) || string.IsNullOrEmpty(rol) || !Guid.TryParse(usuario, out var usuarioId))
            return false;

        Guid? comercioId = null;
        if (Guid.TryParse(principal.FindFirst(ClaimsPlataforma.ComercioId)?.Value, out var comercio))
            comercioId = comercio;

        acceso = new AccesoActual(usuarioId, operadorId, comercioId, rol);
        return true;
    }
}
