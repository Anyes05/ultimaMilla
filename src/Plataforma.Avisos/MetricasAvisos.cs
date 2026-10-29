using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Plataforma.Avisos;

/// <summary>
/// Métricas del tablero técnico que salen del módulo de avisos (letra 6.12): profundidad de las colas
/// y avisos a comercios fallidos. Usa System.Diagnostics.Metrics; quien exporta (OpenTelemetry) lo decide el host.
/// </summary>
public static class MetricasAvisos
{
    public const string NombreMedidor = "Plataforma.Avisos";

    private static readonly Meter Medidor = new(NombreMedidor);

    // Último valor leído por el monitor de colas; los medidores observables lo informan en cada exportación.
    private static readonly ConcurrentDictionary<string, long> MensajesPorCola = new();
    private static readonly ConcurrentDictionary<EstadoEntrega, long> EntregasPorEstado = new();

    private static readonly Counter<long> Intentos = Medidor.CreateCounter<long>(
        "plataforma.avisos.intentos",
        unit: "{intento}",
        description: "Intentos de entrega de avisos a comercios, por resultado (entregado, reintento, fallido).");

    private static readonly Histogram<double> DuracionHttp = Medidor.CreateHistogram<double>(
        "plataforma.avisos.duracion",
        unit: "s",
        description: "Tiempo de la llamada HTTP al sistema del comercio.");

    static MetricasAvisos()
    {
        Medidor.CreateObservableGauge(
            "plataforma.cola.mensajes",
            () => MensajesPorCola.Select(par => new Measurement<long>(par.Value, new KeyValuePair<string, object?>("cola", par.Key))),
            unit: "{mensaje}",
            description: "Mensajes esperando en cada cola de RabbitMQ (profundidad de la cola).");

        Medidor.CreateObservableGauge(
            "plataforma.avisos.entregas",
            () => EntregasPorEstado.Select(par => new Measurement<long>(par.Value,
                new KeyValuePair<string, object?>("estado", par.Key.ToString()))),
            unit: "{entrega}",
            description: "Entregas de avisos por estado actual; Fallido son las que esperan reintento manual.");
    }

    public static void RegistrarIntento(string resultado) =>
        Intentos.Add(1, new KeyValuePair<string, object?>("resultado", resultado));

    public static void RegistrarDuracionHttp(TimeSpan duracion, int? codigoHttp) =>
        DuracionHttp.Record(duracion.TotalSeconds,
            new KeyValuePair<string, object?>("http.response.status_code", codigoHttp ?? 0));

    public static void ActualizarCola(string cola, long mensajes) => MensajesPorCola[cola] = mensajes;

    public static void ActualizarEntregas(IReadOnlyDictionary<EstadoEntrega, long> porEstado)
    {
        foreach (var estado in Enum.GetValues<EstadoEntrega>())
            EntregasPorEstado[estado] = porEstado.GetValueOrDefault(estado);
    }
}
