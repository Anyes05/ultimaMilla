using Plataforma.Domain;

namespace Plataforma.Application;

public interface IHistorialEstadoRepositorio
{
    Task AgregarAsync(HistorialEstado historial, CancellationToken cancellationToken = default);
}
