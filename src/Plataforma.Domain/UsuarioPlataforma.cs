namespace Plataforma.Domain;

public class UsuarioPlataforma
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid? ComercioId { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string NombreUsuario { get; private set; } = string.Empty;
    public string HashClave { get; private set; } = string.Empty;
    public RolUsuario Rol { get; private set; }
    public bool Activo { get; private set; }

    private UsuarioPlataforma()
    {
    }

    public static UsuarioPlataforma Registrar(
        Guid id,
        Guid operadorId,
        string nombre,
        string email,
        string nombreUsuario,
        string hashClave,
        RolUsuario rol,
        Guid? comercioId)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El usuario necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El usuario necesita un operador.");

        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre es requerido.");

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("El email es requerido.");

        if (string.IsNullOrWhiteSpace(nombreUsuario))
            throw new ArgumentException("El nombre de usuario es requerido.");

        if (string.IsNullOrWhiteSpace(hashClave))
            throw new ArgumentException("La clave es requerida.");

        if (rol == RolUsuario.Comercio && comercioId is null)
            throw new ArgumentException("El usuario de comercio necesita un comercio.");

        if (rol != RolUsuario.Comercio && comercioId is not null)
            throw new ArgumentException("Solo el usuario de comercio lleva un comercio.");

        return new UsuarioPlataforma
        {
            Id = id,
            OperadorId = operadorId,
            ComercioId = comercioId,
            Nombre = nombre.Trim(),
            Email = email.Trim(),
            NombreUsuario = nombreUsuario.Trim().ToLowerInvariant(),
            HashClave = hashClave,
            Rol = rol,
            Activo = true
        };
    }
}
