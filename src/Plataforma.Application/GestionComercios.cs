using Plataforma.Domain;

namespace Plataforma.Application;

public interface IComercioRepositorio
{
    Task AgregarAsync(Comercio comercio, CancellationToken cancellationToken = default);
    Task<Comercio?> ObtenerAsync(Guid operadorId, Guid comercioId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Comercio>> ListarAsync(Guid operadorId, CancellationToken cancellationToken = default);
    Task GuardarAsync(CancellationToken cancellationToken = default);
}

public sealed class GestionComercios
{
    private readonly IComercioRepositorio _comercios;

    public GestionComercios(IComercioRepositorio comercios) => _comercios = comercios;

    public async Task<Comercio> RegistrarAsync(
        Guid operadorId,
        string nombreComercial,
        string razonSocial,
        string documentoFiscal,
        string direccionPrincipal,
        CancellationToken cancellationToken = default)
    {
        var comercio = Comercio.Registrar(
            Guid.NewGuid(),
            operadorId,
            nombreComercial,
            razonSocial,
            documentoFiscal,
            direccionPrincipal);
        await _comercios.AgregarAsync(comercio, cancellationToken);
        await _comercios.GuardarAsync(cancellationToken);
        return comercio;
    }

    public async Task ActualizarAsync(
        Guid operadorId,
        Guid comercioId,
        string nombreComercial,
        string razonSocial,
        string documentoFiscal,
        string direccionPrincipal,
        CancellationToken cancellationToken = default)
    {
        var comercio = await _comercios.ObtenerAsync(operadorId, comercioId, cancellationToken)
            ?? throw new InvalidOperationException("El comercio no existe para ese operador.");
        comercio.Actualizar(nombreComercial, razonSocial, documentoFiscal, direccionPrincipal);
        await _comercios.GuardarAsync(cancellationToken);
    }

    public async Task CambiarActivoAsync(
        Guid operadorId,
        Guid comercioId,
        bool activo,
        CancellationToken cancellationToken = default)
    {
        var comercio = await _comercios.ObtenerAsync(operadorId, comercioId, cancellationToken)
            ?? throw new InvalidOperationException("El comercio no existe para ese operador.");
        if (activo)
            comercio.Activar();
        else
            comercio.Desactivar();
        await _comercios.GuardarAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Comercio>> ListarAsync(Guid operadorId, CancellationToken cancellationToken = default) =>
        _comercios.ListarAsync(operadorId, cancellationToken);
}
