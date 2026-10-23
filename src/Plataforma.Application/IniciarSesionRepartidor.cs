using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class IniciarSesionRepartidor
{
    private readonly IniciarSesion _iniciarSesion;
    private readonly IRepartidorRepositorio _repartidores;
    private readonly IEmisorToken _emisor;
    private readonly ProtectorLoginRepartidor _protector;

    public IniciarSesionRepartidor(
        IniciarSesion iniciarSesion,
        IRepartidorRepositorio repartidores,
        IEmisorToken emisor,
        ProtectorLoginRepartidor protector)
    {
        _iniciarSesion = iniciarSesion;
        _repartidores = repartidores;
        _emisor = emisor;
        _protector = protector;
    }

    public async Task<ResultadoInicioRepartidor> Ejecutar(
        string usuario,
        string clave,
        CancellationToken cancellationToken = default)
    {
        if (_protector.EstaBloqueado(usuario))
            return new ResultadoInicioRepartidor.DemasiadosIntentos();

        var resultado = await _iniciarSesion.Ejecutar(usuario, clave, RolUsuario.Repartidor, cancellationToken);
        if (resultado is ResultadoInicio.CredencialesInvalidas)
        {
            _protector.RegistrarFallo(usuario);
            return new ResultadoInicioRepartidor.CredencialesInvalidas();
        }

        if (resultado is ResultadoInicio.RolNoPermitido)
            return new ResultadoInicioRepartidor.RolNoPermitido();

        if (resultado is not ResultadoInicio.Ok ok)
        {
            _protector.RegistrarFallo(usuario);
            return new ResultadoInicioRepartidor.CredencialesInvalidas();
        }

        var repartidor = await _repartidores.ObtenerPorUsuarioAsync(
            ok.Sesion.UsuarioId,
            ok.Sesion.OperadorId,
            cancellationToken);

        if (repartidor is null || !repartidor.Activo)
            return new ResultadoInicioRepartidor.SinPerfil();

        _protector.RegistrarExito(usuario);
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

    public sealed record DemasiadosIntentos : ResultadoInicioRepartidor;
}
