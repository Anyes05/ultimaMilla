using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class ListarEnvios
{
    private readonly IEnvioRepositorio _envios;

    public ListarEnvios(IEnvioRepositorio envios)
    {
        _envios = envios;
    }

    public Task<List<Envio>> Ejecutar(Guid operadorId, Guid? comercioId, CancellationToken cancellationToken = default) =>
        _envios.ListarAsync(operadorId, comercioId, cancellationToken);
}
