using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class IniciarSesionRepartidor
{
    private readonly IniciarSesion _iniciarSesion;
    private readonly IRepartidorRepositorio _repartidores;
    private readonly IEmisorToken _emisor;

    public IniciarSesionRepartidor(
        IniciarSesion iniciarSesion,
        IRepartidorRepositorio repartidores,
        IEmisorToken emisor)
    {
        _iniciarSesion = iniciarSesion;
        _repartidores = repartidores;
        _emisor = emisor;
    }

    public async Task<ResultadoInicioRepartidor> Ejecutar(
        string usuario,
        string clave,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _iniciarSesion.Ejecutar(usuario, clave, RolUsuario.Repartidor, cancellationToken);
        if (resultado is ResultadoInicio.CredencialesInvalidas)
            return new ResultadoInicioRepartidor.CredencialesInvalidas();

        if (resultado is ResultadoInicio.RolNoPermitido)
            return new ResultadoInicioRepartidor.RolNoPermitido();

        if (resultado is not ResultadoInicio.Ok ok)
            return new ResultadoInicioRepartidor.CredencialesInvalidas();

        var repartidor = await _repartidores.ObtenerPorUsuarioAsync(
            ok.Sesion.UsuarioId,
            ok.Sesion.OperadorId,
            cancellationToken);

        if (repartidor is null || !repartidor.Activo)
            return new ResultadoInicioRepartidor.SinPerfil();

        var sesion = ok.Sesion with { RepartidorId = repartidor.Id };
        var token = _emisor.Emitir(sesion);
        return new ResultadoInicioRepartidor.Ok(new SesionRepartidor(
            repartidor.Id,
            sesion.OperadorId,
            repartidor.Nombre,
            Roles.Repartidor,
            token));
    }
}

public abstract record ResultadoInicioRepartidor
{
    private ResultadoInicioRepartidor()
    {
    }

    public sealed record Ok(SesionRepartidor Sesion) : ResultadoInicioRepartidor;

    public sealed record CredencialesInvalidas : ResultadoInicioRepartidor;

    public sealed record RolNoPermitido : ResultadoInicioRepartidor;

    public sealed record SinPerfil : ResultadoInicioRepartidor;
}
