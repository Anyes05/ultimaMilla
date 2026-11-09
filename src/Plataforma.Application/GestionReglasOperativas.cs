using Plataforma.Domain;

namespace Plataforma.Application;

public sealed record NuevaVersionRegla(
    int MaximoIntentos,
    int PlazoEntregaHoras,
    int PlazoReprogramacionHoras,
    bool ExigeFirma,
    bool ExigeFoto,
    bool AdmiteEntregaParcial,
    PoliticaBultosPendientes PoliticaBultosPendientes);

/// <summary>
/// Reglas operativas editables en tiempo de ejecución (RF 4, letra 6.6). Publicar crea una versión nueva y cierra
/// la anterior, que se conserva para los envíos procesados bajo ella.
/// </summary>
public sealed class GestionReglasOperativas
{
    private readonly IReglaOperativaRepositorio _reglas;
    private readonly IOperadorRepositorio _operadores;
    private readonly ICacheLectura _cache;

    public GestionReglasOperativas(
        IReglaOperativaRepositorio reglas,
        IOperadorRepositorio operadores,
        ICacheLectura cache)
    {
        _reglas = reglas;
        _operadores = operadores;
        _cache = cache;
    }

    public Task<IReadOnlyList<ReglaOperativa>> ListarVersionesAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default) =>
        _reglas.ListarVersionesAsync(operadorId, cancellationToken);

    public Task<ReglaOperativa?> ObtenerVigenteAsync(Guid operadorId, CancellationToken cancellationToken = default) =>
        _reglas.ObtenerVigenteAsync(operadorId, DateTime.UtcNow, cancellationToken);

    public async Task PublicarAsync(
        Guid operadorId,
        NuevaVersionRegla datos,
        CancellationToken cancellationToken = default)
    {
        if (datos.PlazoEntregaHoras < 1)
            throw new ArgumentOutOfRangeException(nameof(datos), "El plazo de entrega debe ser de al menos 1 hora.");

        if (datos.PlazoReprogramacionHoras < 0)
            throw new ArgumentOutOfRangeException(nameof(datos), "El plazo de reprogramación no puede ser negativo.");

        var operador = await _operadores.ObtenerParaEditarAsync(operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El operador no existe.");

        var ahora = DateTime.UtcNow;
        var anterior = await _reglas.ObtenerVigenteParaEditarAsync(operadorId, ahora, cancellationToken);
        anterior?.CerrarVigencia(ahora.AddTicks(-1));

        await _reglas.AgregarAsync(
            ReglaOperativa.Crear(
                Guid.NewGuid(),
                operadorId,
                datos.MaximoIntentos,
                datos.PlazoEntregaHoras,
                datos.ExigeFirma,
                datos.ExigeFoto,
                anterior?.ExigeGeocerca ?? false,
                datos.PlazoReprogramacionHoras,
                ahora,
                GestionTarifas.VigenciaAbiertaHasta,
                datos.AdmiteEntregaParcial,
                datos.PoliticaBultosPendientes),
            cancellationToken);

        // La sincronización de la app valida contra el operador: los dos tienen que decir lo mismo.
        operador.CambiarMaximoIntentos(datos.MaximoIntentos);

        await _reglas.GuardarAsync(cancellationToken);
        await _cache.QuitarAsync(ClavesCache.Operador(operadorId), cancellationToken);
    }
}
