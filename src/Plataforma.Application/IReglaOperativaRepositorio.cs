using Plataforma.Domain;

namespace Plataforma.Application;

public interface IReglaOperativaRepositorio
{
    /// <summary>Regla activa en ese instante (letra 6.6: un envío conserva la configuración de su momento).</summary>
    Task<ReglaOperativa?> ObtenerVigenteAsync(
        Guid operadorId,
        DateTime instanteUtc,
        CancellationToken cancellationToken = default);

    Task<ReglaOperativa?> ObtenerVigenteParaEditarAsync(
        Guid operadorId,
        DateTime instanteUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReglaOperativa>> ListarVersionesAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default);

    Task AgregarAsync(ReglaOperativa regla, CancellationToken cancellationToken = default);

    Task GuardarAsync(CancellationToken cancellationToken = default);
}
