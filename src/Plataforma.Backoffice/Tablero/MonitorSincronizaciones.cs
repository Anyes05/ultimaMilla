using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Backoffice.Tablero;

/// <summary>
/// Métrica "sincronizaciones pendientes" del tablero técnico (letra 6.12), por operador.
///
/// El servidor no ve la cola local del teléfono, pero sí sabe qué entregas están en la calle sin resultado:
/// envíos Asignados a ruta o En tránsito. Cuando el repartidor sincroniza la entrega o el intento fallido,
/// el envío sale de ese conjunto. Es la cantidad de resultados que el servidor todavía espera de los dispositivos.
///
/// Cada réplica informa el mismo valor (lo lee de la base); el tablero toma el máximo, no la suma.
/// </summary>
public sealed class MonitorSincronizaciones : BackgroundService
{
    public const string NombreMedidor = "Plataforma.Operacion";

    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(30);
    private static readonly EstadoEnvio[] EnLaCalle = [EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito];

    private static readonly Meter Medidor = new(NombreMedidor);
    private static readonly ConcurrentDictionary<string, long> PendientesPorOperador = new();

    static MonitorSincronizaciones()
    {
        Medidor.CreateObservableGauge(
            "plataforma.sincronizaciones.pendientes",
            () => PendientesPorOperador.Select(par =>
                new Measurement<long>(par.Value, new KeyValuePair<string, object?>("operador", par.Key))),
            unit: "{envio}",
            description: "Envíos en la calle (asignados a ruta o en tránsito) cuyo resultado el repartidor todavía no sincronizó.");
    }

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MonitorSincronizaciones> _logger;

    public MonitorSincronizaciones(IServiceScopeFactory scopes, ILogger<MonitorSincronizaciones> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Medición interna: sin trazas, para no sumar una consulta cada 30 s en Tempo.
                using var sinTrazas = OpenTelemetry.SuppressInstrumentationScope.Begin();
                await LeerAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Es solo medición: si la base no responde, el tablero queda con el último valor.
                _logger.LogWarning(ex, "No se pudieron contar las sincronizaciones pendientes.");
            }

            await Task.Delay(Intervalo, stoppingToken);
        }
    }

    private async Task LeerAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        // Sin usuario autenticado el filtro de operador no restringe: se cuentan todos y se separan por operador.
        var db = scope.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var operadores = await db.Operadores.AsNoTracking()
            .Select(o => new { o.Id, o.Nombre })
            .ToListAsync(stoppingToken);

        var pendientes = await db.Envios.AsNoTracking()
            .Where(e => EnLaCalle.Contains(e.Estado))
            .GroupBy(e => e.OperadorId)
            .Select(g => new { OperadorId = g.Key, Cantidad = g.LongCount() })
            .ToDictionaryAsync(x => x.OperadorId, x => x.Cantidad, stoppingToken);

        // Los operadores sin pendientes se informan en cero, para que el panel no quede vacío.
        foreach (var operador in operadores)
            PendientesPorOperador[operador.Nombre] = pendientes.GetValueOrDefault(operador.Id);
    }
}
