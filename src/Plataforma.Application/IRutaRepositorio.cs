using Plataforma.Domain;

namespace Plataforma.Application;

public interface IRutaRepositorio
{
    Task<Ruta?> ObtenerAsync(Guid operadorId, Guid rutaId, CancellationToken cancellationToken = default);

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

    Task<Parada?> ObtenerParadaPorEnvioAsync(
        Guid operadorId,
        Guid envioId,
        CancellationToken cancellationToken = default);

    Task QuitarParadaAsync(Parada parada, CancellationToken cancellationToken = default);

    Task AgregarParadaAsync(Parada parada, CancellationToken cancellationToken = default);

    Task GuardarAsync(CancellationToken cancellationToken = default);
}
