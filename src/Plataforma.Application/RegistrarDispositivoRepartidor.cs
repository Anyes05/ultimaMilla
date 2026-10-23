using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class RegistrarDispositivoRepartidor
{
    private readonly IDispositivoRepositorio _dispositivos;

    public RegistrarDispositivoRepartidor(IDispositivoRepositorio dispositivos)
    {
        _dispositivos = dispositivos;
    }

    public async Task<DispositivoRepartidor> EjecutarAsync(
        Guid operadorId,
        Guid repartidorId,
        string token,
        string plataforma,
        CancellationToken cancellationToken = default)
    {
        var existente = await _dispositivos.ObtenerPorTokenAsync(
            operadorId, repartidorId, token, cancellationToken);

        if (existente is not null)
            return existente;

        var dispositivo = DispositivoRepartidor.Registrar(
            Guid.NewGuid(),
            operadorId,
            repartidorId,
            token,
            plataforma);

        await _dispositivos.AgregarAsync(dispositivo, cancellationToken);
        await _dispositivos.GuardarAsync(cancellationToken);
        return dispositivo;
    }
}

public sealed class QuitarDispositivoRepartidor
{
    private readonly IDispositivoRepositorio _dispositivos;

    public QuitarDispositivoRepartidor(IDispositivoRepositorio dispositivos)
    {
        _dispositivos = dispositivos;
    }

    public async Task<bool> EjecutarAsync(
        Guid operadorId,
        Guid repartidorId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var existente = await _dispositivos.ObtenerPorTokenAsync(
            operadorId, repartidorId, token, cancellationToken);

        if (existente is null)
            return false;

        await _dispositivos.QuitarAsync(existente, cancellationToken);
        await _dispositivos.GuardarAsync(cancellationToken);
        return true;
    }
}
