namespace Plataforma.Application;

public sealed class ActualizarMarcaOperador
{
    private readonly IOperadorRepositorio _operadores;
    private readonly ICacheLectura _cache;

    public ActualizarMarcaOperador(IOperadorRepositorio operadores, ICacheLectura cache)
    {
        _operadores = operadores;
        _cache = cache;
    }

    public async Task EjecutarAsync(
        Guid operadorId,
        string colorMarca,
        CancellationToken cancellationToken = default)
    {
        var operador = await _operadores.ObtenerParaEditarAsync(operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El operador no existe.");
        operador.CambiarColorMarca(colorMarca);
        await _operadores.GuardarAsync(cancellationToken);
        await _cache.QuitarAsync(ClavesCache.Operador(operadorId), cancellationToken);
    }
}
