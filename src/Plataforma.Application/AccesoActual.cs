using System.Security.Claims;
using Plataforma.Domain;

namespace Plataforma.Application;

public sealed record AccesoActual(Guid UsuarioId, Guid OperadorId, Guid? ComercioId, string Rol, Guid? RepartidorId = null)
{
    public bool EsAdministradorPlataforma =>
        string.Equals(Rol, Roles.AdministradorPlataforma, StringComparison.Ordinal);

    public static bool Intentar(ClaimsPrincipal principal, out AccesoActual acceso)
    {
        acceso = null!;
        var rol = principal.FindFirst(ClaimsPlataforma.Rol)?.Value;
        var usuario = principal.FindFirst(ClaimsPlataforma.UsuarioId)?.Value;

        if (string.IsNullOrEmpty(rol) || !Guid.TryParse(usuario, out var usuarioId))
            return false;

        Guid? comercioId = null;
        if (Guid.TryParse(principal.FindFirst(ClaimsPlataforma.ComercioId)?.Value, out var comercio))
            comercioId = comercio;

        Guid? repartidorId = null;
        if (Guid.TryParse(principal.FindFirst(ClaimsPlataforma.RepartidorId)?.Value, out var repartidor))
            repartidorId = repartidor;

        // Admin de plataforma: sin operador_id (contrato Identity §3.1).
        if (string.Equals(rol, Roles.AdministradorPlataforma, StringComparison.Ordinal))
        {
            acceso = new AccesoActual(usuarioId, Guid.Empty, comercioId, rol, repartidorId);
            return true;
        }

        var operador = principal.FindFirst(ClaimsPlataforma.OperadorId)?.Value;
        if (!Guid.TryParse(operador, out var operadorId) || operadorId == Guid.Empty)
            return false;

        acceso = new AccesoActual(usuarioId, operadorId, comercioId, rol, repartidorId);
        return true;
    }
}
