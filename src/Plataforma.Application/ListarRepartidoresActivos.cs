namespace Plataforma.Application;

public sealed record RepartidorVisible(Guid Id, string Nombre, bool Activo);

/// <summary>Lista repartidores activos del operador autenticado (selector de despacho).</summary>
public sealed class ListarRepartidoresActivos
{
    private readonly IRepartidorRepositorio _repartidores;

    public ListarRepartidoresActivos(IRepartidorRepositorio repartidores)
    {
        _repartidores = repartidores;
    }

    public async Task<IReadOnlyList<RepartidorVisible>> EjecutarAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        var lista = await _repartidores.ListarActivosAsync(operadorId, cancellationToken);
        return lista
            .OrderBy(repartidor => repartidor.Nombre)
            .Select(repartidor => new RepartidorVisible(repartidor.Id, repartidor.Nombre, repartidor.Activo))
            .ToArray();
    }
}
