using Plataforma.Domain;

namespace Plataforma.Application;

public interface ITarifaRepositorio
{
    Task<ReglaTarifaria?> ObtenerActivaAsync(
        Guid operadorId,
        DateTime momentoUtc,
        CancellationToken cancellationToken = default);
}
