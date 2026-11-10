using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class GestionRepartidores
{
    private readonly IRepartidorRepositorio _repartidores;

    public GestionRepartidores(IRepartidorRepositorio repartidores) => _repartidores = repartidores;

    public Task<IReadOnlyList<Repartidor>> ListarAsync(
        Guid operadorId,
        bool soloActivos = false,
        CancellationToken cancellationToken = default) =>
        soloActivos
            ? _repartidores.ListarActivosAsync(operadorId, cancellationToken)
            : _repartidores.ListarAsync(operadorId, cancellationToken);

    public async Task<Repartidor> RegistrarAsync(
        Guid operadorId,
        string nombre,
        string documento,
        string telefono,
        Guid? usuarioId = null,
        CancellationToken cancellationToken = default)
    {
        var repartidor = Repartidor.Registrar(
            Guid.NewGuid(),
            operadorId,
            nombre,
            documento,
            telefono,
            usuarioId);
        await _repartidores.AgregarAsync(repartidor, cancellationToken);
        await _repartidores.GuardarAsync(cancellationToken);
        return repartidor;
    }

    public async Task ActualizarAsync(
        Guid operadorId,
        Guid repartidorId,
        string nombre,
        string documento,
        string telefono,
        Guid? usuarioId = null,
        CancellationToken cancellationToken = default)
    {
        var repartidor = await _repartidores.ObtenerParaEditarAsync(repartidorId, operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El repartidor no existe para ese operador.");
        repartidor.Actualizar(nombre, documento, telefono, usuarioId);
        await _repartidores.GuardarAsync(cancellationToken);
    }

    public async Task CambiarActivoAsync(
        Guid operadorId,
        Guid repartidorId,
        bool activo,
        CancellationToken cancellationToken = default)
    {
        var repartidor = await _repartidores.ObtenerParaEditarAsync(repartidorId, operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El repartidor no existe para ese operador.");
        if (activo)
            repartidor.Activar();
        else
            repartidor.Desactivar();
        await _repartidores.GuardarAsync(cancellationToken);
    }
}
