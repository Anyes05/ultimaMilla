using Plataforma.Domain;

namespace Plataforma.Application;

public interface IEventoSincronizacionRepositorio
{
    Task<EventoSincronizacionProcesado?> ObtenerAsync(Guid eventoId, CancellationToken cancellationToken = default);

    Task AgregarAsync(EventoSincronizacionProcesado evento, CancellationToken cancellationToken = default);
}
