using Plataforma.Domain;

namespace Plataforma.Application;

public interface IRutaRepositorio
{
    Task<IReadOnlyList<Ruta>> ObtenerDeHoyAsync(
        Guid operadorId,
        Guid repartidorId,
        DateOnly fecha,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Ruta>> ObtenerDelDiaAsync(
        Guid operadorId,
        DateOnly fecha,
        CancellationToken cancellationToken = default);

    Task GuardarDespachoAsync(
        Ruta ruta,
        IReadOnlyList<Parada> paradas,
        CancellationToken cancellationToken = default);
}
