using Plataforma.Domain;

namespace Plataforma.Application;

public interface ITarifaRepositorio
{
    Task<ReglaTarifaria?> ObtenerActivaAsync(
        Guid operadorId,
        DateTime momentoUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(VigenciaTarifa Vigencia, ReglaTarifaria Regla)>> ListarVersionesAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default);

    Task<VigenciaTarifa?> ObtenerVigenciaActivaParaEditarAsync(
        Guid operadorId,
        DateTime momentoUtc,
        CancellationToken cancellationToken = default);

    Task AgregarReglaAsync(ReglaTarifaria regla, CancellationToken cancellationToken = default);

    Task AgregarVigenciaAsync(VigenciaTarifa vigencia, CancellationToken cancellationToken = default);

    Task GuardarAsync(CancellationToken cancellationToken = default);
}
