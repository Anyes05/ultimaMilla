using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class GestionUsuarios
{
    private static readonly HashSet<RolUsuario> RolesPermitidos =
    [
        RolUsuario.Administrador,
        RolUsuario.Despachador,
        RolUsuario.OperarioDeposito,
        RolUsuario.Comercio,
        RolUsuario.Repartidor
    ];

    private readonly IUsuarioRepositorio _usuarios;
    private readonly IAutenticacionIdentity _identity;
    private readonly IVerificadorClave _claves;

    public GestionUsuarios(
        IUsuarioRepositorio usuarios,
        IAutenticacionIdentity identity,
        IVerificadorClave claves)
    {
        _usuarios = usuarios;
        _identity = identity;
        _claves = claves;
    }

    public Task<IReadOnlyList<UsuarioPlataforma>> ListarAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        _usuarios.ListarAsync(operadorId, cancellationToken);

    public async Task<UsuarioPlataforma> RegistrarAsync(
        Guid operadorId,
        string nombre,
        string email,
        string nombreUsuario,
        string clave,
        RolUsuario rol,
        Guid? comercioId = null,
        CancellationToken cancellationToken = default)
    {
        if (!RolesPermitidos.Contains(rol))
            throw new ArgumentException($"El rol {rol} no se gestiona desde el ABM del operador.");

        if (string.IsNullOrWhiteSpace(clave) || clave.Length < 6)
            throw new ArgumentException("La clave debe tener al menos 6 caracteres.");

        var normalizado = nombreUsuario.Trim().ToLowerInvariant();
        if (await _usuarios.BuscarPorNombreAsync(normalizado, cancellationToken) is not null)
            throw new InvalidOperationException("Ya existe un usuario con ese nombre.");

        var id = Guid.NewGuid();
        var usuario = UsuarioPlataforma.Registrar(
            id,
            operadorId,
            nombre,
            email,
            normalizado,
            _claves.Generar(clave),
            rol,
            comercioId);

        await _usuarios.AgregarAsync(usuario, cancellationToken);
        await _identity.CrearAsync(id, usuario.NombreUsuario, usuario.Email, clave, cancellationToken);
        await _usuarios.GuardarAsync(cancellationToken);
        return usuario;
    }

    public async Task ActualizarAsync(
        Guid operadorId,
        Guid usuarioId,
        string nombre,
        string email,
        RolUsuario rol,
        Guid? comercioId = null,
        CancellationToken cancellationToken = default)
    {
        if (!RolesPermitidos.Contains(rol))
            throw new ArgumentException($"El rol {rol} no se gestiona desde el ABM del operador.");

        var usuario = await _usuarios.ObtenerAsync(operadorId, usuarioId, cancellationToken)
            ?? throw new InvalidOperationException("El usuario no existe para ese operador.");

        usuario.Actualizar(nombre, email, rol, comercioId);
        await _usuarios.GuardarAsync(cancellationToken);
    }

    public async Task CambiarActivoAsync(
        Guid operadorId,
        Guid usuarioId,
        bool activo,
        CancellationToken cancellationToken = default)
    {
        var usuario = await _usuarios.ObtenerAsync(operadorId, usuarioId, cancellationToken)
            ?? throw new InvalidOperationException("El usuario no existe para ese operador.");

        if (activo)
            usuario.Activar();
        else
            usuario.Desactivar();

        await _usuarios.GuardarAsync(cancellationToken);
    }
}
