using Plataforma.Domain;

namespace Plataforma.Application;

public interface IOutboxRepositorio
{
    /// <summary>Agrega el mensaje al contexto; no guarda. Debe ir con el mismo SaveChanges del cambio de dominio.</summary>
    Task AgregarAsync(MensajeOutbox mensaje, CancellationToken cancellationToken = default);

    Task<bool> ExisteAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MensajeOutbox>> ListarPendientesAsync(
        int maximo,
        CancellationToken cancellationToken = default);

    Task<MensajeOutbox?> ObtenerAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task GuardarAsync(CancellationToken cancellationToken = default);
}
