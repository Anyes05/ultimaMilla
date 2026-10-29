using Plataforma.Domain;

namespace Plataforma.Application;

public interface IIntentoEntregaRepositorio
{
    Task AgregarAsync(IntentoEntrega intento, CancellationToken cancellationToken = default);
}
