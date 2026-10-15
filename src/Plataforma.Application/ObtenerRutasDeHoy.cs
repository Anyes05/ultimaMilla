using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plataforma.Application;

public sealed class ObtenerRutasDeHoy
{
    public static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IRutaRepositorio _rutas;
    private readonly IEnvioRepositorio _envios;
    private readonly ConfiguracionOperador _operadores;
    private readonly ICacheLectura _cache;

    public ObtenerRutasDeHoy(
        IRutaRepositorio rutas,
        IEnvioRepositorio envios,
        ConfiguracionOperador operadores,
        ICacheLectura cache)
    {
        _rutas = rutas;
        _envios = envios;
        _operadores = operadores;
        _cache = cache;
    }

    public async Task<IReadOnlyList<RutaDelDia>> Ejecutar(
        Guid operadorId,
        Guid repartidorId,
        DateOnly fecha,
        CancellationToken cancellationToken = default)
    {
        var clave = Clave(operadorId, repartidorId, fecha);
        var cacheado = await _cache.LeerAsync(clave, cancellationToken);
        if (cacheado is not null)
            return JsonSerializer.Deserialize<List<RutaDelDia>>(cacheado, OpcionesJson) ?? [];

        var operador = await _operadores.ObtenerAsync(operadorId, cancellationToken);
        var rutas = await _rutas.ObtenerDeHoyAsync(operadorId, repartidorId, fecha, cancellationToken);
        var resultado = new List<RutaDelDia>(rutas.Count);
        foreach (var ruta in rutas)
        {
            var envios = await _envios.ObtenerPorRutaAsync(operadorId, ruta.Id, cancellationToken);
            resultado.Add(new RutaDelDia(
                ruta.Id,
                ruta.Fecha,
                operador,
                envios.Select(EnvioDeRuta.Desde).ToList()));
        }

        await _cache.GuardarAsync(clave, JsonSerializer.Serialize(resultado, OpcionesJson), TimeSpan.FromHours(12), cancellationToken);
        return resultado;
    }

    public static string Clave(Guid operadorId, Guid repartidorId, DateOnly fecha) =>
        $"rutas:hoy:{operadorId:N}:{repartidorId:N}:{fecha:yyyy-MM-dd}";
}
