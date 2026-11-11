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
        CancellationToken cancellationToken = default,
        string? telefonoContacto = null,
        string? emailContacto = null)
    {
        var operador = await _operadores.ObtenerParaEditarAsync(operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El operador no existe.");
        operador.CambiarColorMarca(colorMarca);
        // Sin datos de contacto (la API actual solo manda el color) se conservan los que había.
        if (telefonoContacto is not null || emailContacto is not null)
            operador.CambiarContacto(telefonoContacto ?? operador.TelefonoContacto, emailContacto ?? operador.EmailContacto);
        await _operadores.GuardarAsync(cancellationToken);
        await _cache.QuitarAsync(ClavesCache.Operador(operadorId), cancellationToken);
    }
}
