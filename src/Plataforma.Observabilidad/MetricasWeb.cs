using System.Diagnostics.Metrics;

namespace Plataforma.Observabilidad;

/// <summary>
/// Métricas propias de Backoffice y Portal. Cada réplica informa sus contadores y el tablero los suma:
/// así la relación de aciertos del caché es la de todo el sistema, no la del pod que atendió /tecnico/cache.
/// </summary>
public static class MetricasWeb
{
    private static readonly Meter Medidor = new("Plataforma.Web");

    /// <summary>Publica los aciertos y fallos del caché distribuido. Se llama una vez al arrancar.</summary>
    public static void RegistrarCache(Func<(long Aciertos, long Fallos)> leer)
    {
        Medidor.CreateObservableCounter(
            "plataforma.cache.aciertos",
            () => leer().Aciertos,
            unit: "{lectura}",
            description: "Lecturas del caché que encontraron el dato.");

        Medidor.CreateObservableCounter(
            "plataforma.cache.fallos",
            () => leer().Fallos,
            unit: "{lectura}",
            description: "Lecturas del caché que no encontraron el dato y fueron a la base.");
    }
}
