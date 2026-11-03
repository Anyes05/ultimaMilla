using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

/// <summary>
/// Envuelve el caché (Redis o memoria) para dos exigencias de la letra 6.7 y de sus aclaraciones:
/// <list type="bullet">
/// <item>Métrica de aciertos y fallos <b>por caso de uso</b> (OpenTelemetry, medidor "Plataforma.Cache").</item>
/// <item>Si el caché no está disponible, el sistema sigue contra la base: el error cuenta como fallo y,
/// durante <see cref="Pausa"/>, ni se intenta Redis, para no sumar su timeout a cada request.</item>
/// </list>
/// No cambia el comportamiento de los casos de uso: siguen viendo <see cref="ICacheLectura"/>.
/// </summary>
public sealed class CacheLecturaInstrumentada : ICacheLectura
{
    public const string NombreMedidor = "Plataforma.Cache";

    /// <summary>Tiempo sin consultar el caché después de un error.</summary>
    public static readonly TimeSpan Pausa = TimeSpan.FromSeconds(15);

    private static readonly Meter Medidor = new(NombreMedidor);

    private static readonly Counter<long> Lecturas = Medidor.CreateCounter<long>(
        "plataforma.cache.lecturas",
        unit: "{lectura}",
        description: "Lecturas del caché por caso de uso y resultado (acierto, fallo, error).");

    private readonly ICacheLectura _interno;
    private readonly ILogger<CacheLecturaInstrumentada> _logger;
    private readonly TimeProvider _reloj;
    private long _aciertos;
    private long _fallos;
    private long _pausadoHastaTicks;

    public CacheLecturaInstrumentada(
        ICacheLectura interno,
        ILogger<CacheLecturaInstrumentada> logger,
        TimeProvider? reloj = null)
    {
        _interno = interno;
        _logger = logger;
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<string?> LeerAsync(string clave, CancellationToken cancellationToken = default)
    {
        var casoUso = CasoDeUso(clave);
        if (EnPausa())
        {
            Registrar(casoUso, "error");
            return null;
        }

        try
        {
            var valor = await _interno.LeerAsync(clave, cancellationToken);
            Registrar(casoUso, valor is null ? "fallo" : "acierto");
            return valor;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Pausar(ex, "leer");
            Registrar(casoUso, "error");
            return null;
        }
    }

    public async Task GuardarAsync(string clave, string valor, TimeSpan vigencia, CancellationToken cancellationToken = default)
    {
        if (EnPausa())
            return;

        try
        {
            await _interno.GuardarAsync(clave, valor, vigencia, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin caché el dato se vuelve a leer de la base: no hay nada que corregir.
            Pausar(ex, "guardar");
        }
    }

    public async Task QuitarAsync(string clave, CancellationToken cancellationToken = default)
    {
        // La invalidación se intenta aunque esté en pausa: si el caché volvió, evita servir un dato viejo.
        try
        {
            await _interno.QuitarAsync(clave, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Si el caché no responde, la entrada vieja expira por su vigencia.
            Pausar(ex, "invalidar");
        }
    }

    /// <summary>Totales de esta instancia, incluidas las lecturas que no llegaron al caché (para /tecnico/cache).</summary>
    public EstadisticasCache Estadisticas() =>
        new(Interlocked.Read(ref _aciertos), Interlocked.Read(ref _fallos));

    /// <summary>
    /// Caso de uso a partir de la clave (convención de <see cref="ClavesCache"/>). Se buscan fragmentos y no
    /// prefijos fijos, así sigue funcionando si las claves pasan a llevar el operador adelante.
    /// </summary>
    public static string CasoDeUso(string clave)
    {
        if (clave.Contains("seguimiento:", StringComparison.Ordinal))
            return "seguimiento";
        if (clave.Contains("tarifa", StringComparison.Ordinal))
            return "tarifas";
        if (clave.StartsWith("operador:", StringComparison.Ordinal))
            return "configuracion-operador";
        return "otro";
    }

    private void Registrar(string casoUso, string resultado)
    {
        if (resultado == "acierto")
            Interlocked.Increment(ref _aciertos);
        else
            Interlocked.Increment(ref _fallos);

        Lecturas.Add(1,
            new KeyValuePair<string, object?>("caso_uso", casoUso),
            new KeyValuePair<string, object?>("resultado", resultado));
    }

    private bool EnPausa() => _reloj.GetUtcNow().UtcTicks < Interlocked.Read(ref _pausadoHastaTicks);

    private void Pausar(Exception ex, string operacion)
    {
        var hasta = _reloj.GetUtcNow().Add(Pausa).UtcTicks;
        // Solo el primer error de cada pausa queda en el log, para no inundarlo mientras el caché está caído.
        if (Interlocked.Exchange(ref _pausadoHastaTicks, hasta) < _reloj.GetUtcNow().UtcTicks)
            _logger.LogWarning(ex, "Caché no disponible al {Operacion}: se sigue contra la base por {Segundos} s.",
                operacion, Pausa.TotalSeconds);
    }
}

public static class CacheLecturaInstrumentadaExtensions
{
    /// <summary>
    /// Envuelve el <see cref="ICacheLectura"/> ya registrado (después de AddInfrastructure).
    /// Lo llama cada host web en su raíz de composición.
    /// </summary>
    public static IServiceCollection AddCacheInstrumentada(this IServiceCollection services)
    {
        var registrado = services.LastOrDefault(s => s.ServiceType == typeof(ICacheLectura))
            ?? throw new InvalidOperationException("Primero hay que registrar ICacheLectura (AddInfrastructure).");

        services.Remove(registrado);
        services.AddSingleton<ICacheLectura>(proveedor => new CacheLecturaInstrumentada(
            (ICacheLectura)(registrado.ImplementationInstance
                ?? registrado.ImplementationFactory?.Invoke(proveedor)
                ?? ActivatorUtilities.CreateInstance(proveedor, registrado.ImplementationType!)),
            proveedor.GetRequiredService<ILogger<CacheLecturaInstrumentada>>()));
        return services;
    }
}
