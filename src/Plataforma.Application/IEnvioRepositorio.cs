using Plataforma.Domain;

namespace Plataforma.Application;

public interface IEnvioRepositorio
{
    Task AgregarAsync(Envio envio);
    Task<List<Envio>> ObtenerTodosAsync();
    Task<Envio?> ObtenerPorOperadorAsync(Guid envioId, Guid operadorId, CancellationToken cancellationToken = default);
    Task<Envio?> ObtenerPorTokenAsync(string tokenSeguimientoPublico, CancellationToken cancellationToken = default);
    Task<Envio?> ObtenerPorNumeroAsync(string numeroSeguimiento, CancellationToken cancellationToken = default);
    Task<Envio?> ObtenerPorClaveExternaAsync(
        Guid operadorId,
        Guid comercioId,
        string claveExterna,
        CancellationToken cancellationToken = default);
    Task<List<Envio>> ListarAsync(Guid operadorId, Guid? comercioId, CancellationToken cancellationToken = default);

    /// <summary>Envíos del comercio dados de alta desde el instante indicado (bonificaciones por volumen del mes).</summary>
    Task<int> ContarDelComercioDesdeAsync(Guid operadorId, Guid comercioId, DateTime desdeUtc, CancellationToken cancellationToken = default);
    Task<ResultadoPaginado<Envio>> ListarPaginadoAsync(
        Guid operadorId,
        Guid? comercioId,
        EstadoEnvio? estado,
        int pagina,
        int tamanoPagina,
        CancellationToken cancellationToken = default);
    Task<List<Envio>> ObtenerPorRutaAsync(Guid operadorId, Guid rutaId, CancellationToken cancellationToken = default);
    Task<List<Envio>> ObtenerPorRutaParaEditarAsync(Guid operadorId, Guid rutaId, CancellationToken cancellationToken = default);
    Task<Envio?> ObtenerEnRutaAsync(Guid envioId, Guid operadorId, IReadOnlyCollection<Guid> rutasId, CancellationToken cancellationToken = default);
    Task GuardarAsync(CancellationToken cancellationToken = default);
}

public sealed record ResultadoPaginado<T>(IReadOnlyList<T> Items, int Total, int Pagina, int TamanoPagina);