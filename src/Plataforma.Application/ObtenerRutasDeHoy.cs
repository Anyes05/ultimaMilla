using System.Text.Json;
using System.Text.Json.Serialization;
using Plataforma.Domain;

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
    private readonly IBanderasOperador _banderas;
    private readonly ICacheLectura _cache;
    private readonly IBultoRepositorio? _bultos;
    private readonly IReglaOperativaRepositorio? _reglas;
    private readonly IRetiroRepositorio? _retiros;
    private readonly ICoberturaRepositorio? _cobertura;

    public ObtenerRutasDeHoy(
        IRutaRepositorio rutas,
        IEnvioRepositorio envios,
        ConfiguracionOperador operadores,
        IBanderasOperador banderas,
        ICacheLectura cache,
        IBultoRepositorio? bultos = null,
        IReglaOperativaRepositorio? reglas = null,
        IRetiroRepositorio? retiros = null,
        ICoberturaRepositorio? cobertura = null)
    {
        _retiros = retiros;
        _cobertura = cobertura;
        _rutas = rutas;
        _envios = envios;
        _operadores = operadores;
        _banderas = banderas;
        _cache = cache;
        _bultos = bultos;
        _reglas = reglas;
    }

    public async Task<IReadOnlyList<RutaDelDia>> Ejecutar(
        Guid operadorId,
        Guid repartidorId,
        DateOnly fecha,
        CancellationToken cancellationToken = default)
    {
        var operadorBase = await _operadores.ObtenerAsync(operadorId, cancellationToken);
        var activas = new List<string>();
        foreach (var def in CatalogoBanderas.Todas)
        {
            if (await _banderas.EstaActivaAsync(operadorId, def.Clave, cancellationToken))
                activas.Add(def.Clave);
        }
        var regla = _reglas is null
            ? null
            : await _reglas.ObtenerVigenteAsync(operadorId, DateTime.UtcNow, cancellationToken);
        var operador = operadorBase with
        {
            Funcionalidades = activas,
            AdmiteEntregaParcial = regla?.AdmiteEntregaParcial ?? false,
            ExigeFirma = regla?.ExigeFirma ?? false,
            ExigeFoto = regla?.ExigeFoto ?? false,
            MotivosNoEntrega = regla?.MotivosNoEntrega ?? ReglaOperativa.MotivosNoEntregaPredeterminados,
            PoliticaBultosPendientes = regla?.PoliticaBultosPendientes ?? PoliticaBultosPendientes.NuevaVisitaSinRecargo,
            ExigeDocumentoReceptor = regla?.ExigeDocumentoReceptor ?? false
        };

        var clave = Clave(operadorId, repartidorId, fecha);
        var cacheado = await _cache.LeerAsync(clave, cancellationToken);
        if (cacheado is not null)
        {
            var rutasCacheadas = JsonSerializer.Deserialize<List<RutaDelDia>>(cacheado, OpcionesJson) ?? [];
            return rutasCacheadas.Select(ruta => ruta with { Operador = operador }).ToList();
        }

        var rutas = await _rutas.ObtenerDeHoyAsync(operadorId, repartidorId, fecha, cancellationToken);
        var resultado = new List<RutaDelDia>(rutas.Count);
        // RF 40: los retiros asignados a estas rutas, con su parada y su franja.
        var retirosDelDia = _retiros is null || rutas.Count == 0
            ? []
            : (await _retiros.ListarAsync(operadorId, null, cancellationToken))
                .Where(retiro => retiro.RutaId is Guid rutaId && rutas.Any(ruta => ruta.Id == rutaId)
                                 && retiro.Estado is EstadoRetiro.Programado or EstadoRetiro.EnCurso
                                     or EstadoRetiro.Completado or EstadoRetiro.NoRetirado)
                .ToList();
        var franjas = retirosDelDia.Any(retiro => retiro.FranjaId is not null) && _cobertura is not null
            ? (await _cobertura.ListarTodasLasFranjasAsync(operadorId, cancellationToken)).ToDictionary(franja => franja.Id)
            : new Dictionary<Guid, FranjaHoraria>();
        foreach (var ruta in rutas)
        {
            var envios = await _envios.ObtenerPorRutaAsync(operadorId, ruta.Id, cancellationToken);
            var enviosDeRuta = new List<EnvioDeRuta>(envios.Count);
            foreach (var envio in envios)
            {
                IReadOnlyList<BultoDeRuta> bultos = _bultos is null
                    ? []
                    : (await _bultos.ListarPorEnvioAsync(operadorId, envio.Id, cancellationToken))
                        .Select(bulto => new BultoDeRuta(
                            bulto.Id, bulto.Codigo, bulto.PesoKg, bulto.Estado, bulto.MotivoNoEntrega))
                        .ToArray();
                enviosDeRuta.Add(EnvioDeRuta.Desde(envio) with { Bultos = bultos });
            }
            var retirosDeRuta = retirosDelDia
                .Where(retiro => retiro.RutaId == ruta.Id)
                .OrderBy(retiro => retiro.PosicionEnRuta ?? int.MaxValue)
                .ThenBy(retiro => retiro.CreadoEn)
                .Select(retiro =>
                {
                    var franja = retiro.FranjaId is Guid franjaId && franjas.TryGetValue(franjaId, out var encontrada) ? encontrada : null;
                    return new RetiroDeRuta(
                        retiro.Id, retiro.Direccion, retiro.Contacto, retiro.Motivo, retiro.Observaciones,
                        retiro.CantidadBultos, retiro.PesoKg, retiro.PosicionEnRuta, franja?.Desde, franja?.Hasta, retiro.Estado);
                })
                .ToList();
            resultado.Add(new RutaDelDia(
                ruta.Id,
                ruta.Fecha,
                operador,
                enviosDeRuta,
                retirosDeRuta,
                ruta.IniciadaEn));
        }

        await _cache.GuardarAsync(clave, JsonSerializer.Serialize(resultado, OpcionesJson), TimeSpan.FromSeconds(30), cancellationToken);
        return resultado;
    }

    public static string Clave(Guid operadorId, Guid repartidorId, DateOnly fecha) =>
        $"rutas:hoy:v2:{operadorId:N}:{repartidorId:N}:{fecha:yyyy-MM-dd}";
}
