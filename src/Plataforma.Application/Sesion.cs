using Plataforma.Domain;

namespace Plataforma.Application;

public sealed record Sesion(
    Guid UsuarioId,
    string NombreUsuario,
    RolUsuario Rol,
    Guid OperadorId,
    Guid? ComercioId,
    Guid? RepartidorId = null)
{
    public string RolClaim => Roles.De(Rol);
}

public abstract record ResultadoInicio
{
    private ResultadoInicio()
    {
    }

    public sealed record Ok(Sesion Sesion) : ResultadoInicio;

    public sealed record CredencialesInvalidas : ResultadoInicio;

    public sealed record RolNoPermitido : ResultadoInicio;
}
