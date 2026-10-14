using Plataforma.Domain;
//lolo
namespace Plataforma.Application;

public interface IEnvioRepositorio
{
    Task AgregarAsync(Envio envio);
    Task<List<Envio>> ObtenerTodosAsync();
    Task<Envio?> ObtenerPorOperadorAsync(Guid envioId, Guid operadorId, CancellationToken cancellationToken = default);
    Task<List<Envio>> ListarAsync(Guid operadorId, Guid? comercioId, CancellationToken cancellationToken = default);
    Task<List<Envio>> ObtenerPorRutaAsync(Guid operadorId, Guid rutaId, CancellationToken cancellationToken = default);
    Task<Envio?> ObtenerEnRutaAsync(Guid envioId, Guid operadorId, IReadOnlyCollection<Guid> rutasId, CancellationToken cancellationToken = default);
    Task GuardarAsync(CancellationToken cancellationToken = default);
}