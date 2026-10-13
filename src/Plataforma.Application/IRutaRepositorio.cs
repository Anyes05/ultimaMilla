using Plataforma.Domain;

namespace Plataforma.Application;

public interface IRutaRepositorio
{
    Task<IReadOnlyList<Ruta>> ObtenerDeHoyAsync(
        Guid operadorId,
        Guid repartidorId,
        DateOnly fecha,
        CancellationToken cancellationToken = default);
}
