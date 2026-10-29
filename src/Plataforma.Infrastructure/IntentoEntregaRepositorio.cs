using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class IntentoEntregaRepositorio(PlataformaDbContext context) : IIntentoEntregaRepositorio
{
    public async Task AgregarAsync(IntentoEntrega intento, CancellationToken cancellationToken = default)
    {
        await context.IntentosEntrega.AddAsync(intento, cancellationToken);
    }
}
