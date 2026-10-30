using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Avisos;

namespace Plataforma.Worker;

/// <summary>
/// Alimenta el tablero técnico (letra 6.12): cada pocos segundos lee cuántos mensajes esperan en cada cola
/// de avisos y cuántas entregas hay por estado. Solo lee: declara las colas en modo pasivo y cuenta en la base.
/// </summary>
public sealed class MonitorColas : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly OpcionesRabbitMq _rabbit;
    private readonly OpcionesAvisos _avisos;
    private readonly ILogger<MonitorColas> _logger;

    public MonitorColas(
        IServiceScopeFactory scopes,
        IOptions<OpcionesRabbitMq> rabbit,
        IOptions<OpcionesAvisos> avisos,
        ILogger<MonitorColas> logger)
    {
        _scopes = scopes;
        _rabbit = rabbit.Value;
        _avisos = avisos.Value;
        _logger = logger;
    }

    private IEnumerable<string> Colas =>
        new[] { TopologiaAvisos.ColaEventos, TopologiaAvisos.ColaEntregas, TopologiaAvisos.ColaFallidos }
            .Concat(_avisos.Esperas.Select(TopologiaAvisos.ColaEspera));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_rabbit.Configurado)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Es medición interna: sin trazas, para que cada 15 s no aparezca una consulta en Tempo.
                using var sinTrazas = OpenTelemetry.SuppressInstrumentationScope.Begin();
                await LeerColasAsync(stoppingToken);
                await LeerEntregasAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Es solo medición: si RabbitMQ o la base no responden, el tablero queda con el último valor.
                _logger.LogWarning(ex, "No se pudo leer la profundidad de las colas para las métricas.");
            }

            await Task.Delay(Intervalo, stoppingToken);
        }
    }

    private async Task LeerColasAsync(CancellationToken stoppingToken)
    {
        await using var conexion = await TopologiaAvisos.CrearFabrica(_rabbit, "plataforma-worker-monitor")
            .CreateConnectionAsync(stoppingToken);

        foreach (var cola in Colas)
        {
            // Un canal por cola: si una no existe todavía, RabbitMQ cierra ese canal y las demás se siguen leyendo.
            try
            {
                await using var canal = await conexion.CreateChannelAsync(cancellationToken: stoppingToken);
                var estado = await canal.QueueDeclarePassiveAsync(cola, stoppingToken);
                MetricasAvisos.ActualizarCola(cola, estado.MessageCount);
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                MetricasAvisos.ActualizarCola(cola, 0);
            }
        }
    }

    private async Task LeerEntregasAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AvisosDbContext>();

        var porEstado = await db.Entregas.AsNoTracking()
            .GroupBy(e => e.Estado)
            .Select(g => new { Estado = g.Key, Cantidad = g.LongCount() })
            .ToDictionaryAsync(x => x.Estado, x => x.Cantidad, stoppingToken);

        MetricasAvisos.ActualizarEntregas(porEstado);
    }
}
