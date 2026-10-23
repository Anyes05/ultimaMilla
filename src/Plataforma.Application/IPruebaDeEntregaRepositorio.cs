using Plataforma.Domain;

namespace Plataforma.Application;

public interface IPruebaDeEntregaRepositorio
{
    Task AgregarAsync(PruebaDeEntrega prueba, CancellationToken cancellationToken = default);
}
