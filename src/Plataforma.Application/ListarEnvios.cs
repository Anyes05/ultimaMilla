using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>
/// Listado de envíos del operador autenticado. El comercio solo ve los propios.
/// Nadia conecta pantallas a este caso de uso; no debe listar con ObtenerTodosAsync.
/// </summary>
public sealed class ListarEnvios
{
    private readonly IEnvioRepositorio _envios;

    public ListarEnvios(IEnvioRepositorio envios)
    {
        _envios = envios;
    }

    public Task<List<Envio>> Ejecutar(Guid operadorId, Guid? comercioId = null, CancellationToken cancellationToken = default) =>
        _envios.ListarAsync(operadorId, comercioId, cancellationToken);

    public Task<List<Envio>> Ejecutar(AccesoActual acceso, CancellationToken cancellationToken = default)
    {
        Guid? comercioId = acceso.Rol == Roles.Comercio ? acceso.ComercioId : null;
        return Ejecutar(acceso.OperadorId, comercioId, cancellationToken);
    }
}
