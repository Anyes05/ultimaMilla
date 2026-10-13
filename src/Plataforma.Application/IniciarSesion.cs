using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class IniciarSesion
{
    private readonly IUsuarioRepositorio _usuarios;
    private readonly IVerificadorClave _claves;

    public IniciarSesion(IUsuarioRepositorio usuarios, IVerificadorClave claves)
    {
        _usuarios = usuarios;
        _claves = claves;
    }

    public async Task<ResultadoInicio> Ejecutar(string usuario, string clave, RolUsuario rolEsperado, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(clave))
            return new ResultadoInicio.CredencialesInvalidas();

        var encontrado = await _usuarios.BuscarPorNombreAsync(usuario, cancellationToken);
        if (encontrado is null || !_claves.Coincide(clave, encontrado.HashClave))
            return new ResultadoInicio.CredencialesInvalidas();

        if (encontrado.Rol != rolEsperado)
            return new ResultadoInicio.RolNoPermitido();

        return new ResultadoInicio.Ok(new Sesion(
            encontrado.Id,
            encontrado.NombreUsuario,
            encontrado.Rol,
            encontrado.OperadorId,
            encontrado.ComercioId));
    }
}
