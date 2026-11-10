using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class IniciarSesion
{
    private readonly IUsuarioRepositorio _usuarios;
    private readonly IAutenticacionIdentity _identity;
    private readonly IComercioRepositorio _comercios;

    public IniciarSesion(IUsuarioRepositorio usuarios, IAutenticacionIdentity identity, IComercioRepositorio comercios)
    {
        _usuarios = usuarios;
        _identity = identity;
        _comercios = comercios;
    }

    public async Task<ResultadoInicio> Ejecutar(string usuario, string clave, RolUsuario rolEsperado, CancellationToken cancellationToken = default)
    {
        var resultado = await Ejecutar(usuario, clave, cancellationToken);
        if (resultado is not ResultadoInicio.Ok ok)
            return resultado;

        if (ok.Sesion.Rol != rolEsperado)
            return new ResultadoInicio.RolNoPermitido();

        return ok;
    }

    /// <summary>
    /// Login sin forzar rol: emite la sesión con el rol real del usuario (claims listos para Nadia).
    /// </summary>
    public async Task<ResultadoInicio> Ejecutar(string usuario, string clave, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(clave))
            return new ResultadoInicio.CredencialesInvalidas();

        var encontrado = await _usuarios.BuscarPorNombreAsync(usuario, cancellationToken);
        if (encontrado is null || !encontrado.Activo)
            return new ResultadoInicio.CredencialesInvalidas();

        // Un comercio dado de baja no opera (RF 1): sus usuarios no pueden entrar.
        if (encontrado.ComercioId is Guid comercioId && !await _comercios.EstaActivoAsync(comercioId, cancellationToken))
            return new ResultadoInicio.CredencialesInvalidas();

        var ok = await _identity.VerificarAsync(
            encontrado.Id,
            encontrado.NombreUsuario,
            encontrado.Email,
            clave,
            encontrado.HashClave,
            cancellationToken);
        if (!ok)
            return new ResultadoInicio.CredencialesInvalidas();

        return new ResultadoInicio.Ok(new Sesion(
            encontrado.Id,
            encontrado.NombreUsuario,
            encontrado.Rol,
            encontrado.OperadorId,
            encontrado.ComercioId));
    }
}
