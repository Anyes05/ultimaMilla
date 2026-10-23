using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma.Application;
using StackExchange.Redis;

namespace Plataforma.Infrastructure;

/// <summary>
/// Drena outbox pendiente hacia RabbitMQ.
/// Usa un lock Redis (letra 6.13) para que solo una réplica ejecute la tarea programada.
/// </summary>
public sealed class OutboxPublicadorBackground : BackgroundService
{
    private const string ClaveLock = "plataforma:outbox:publicador";
    private static readonly TimeSpan TtlLock = TimeSpan.FromSeconds(20);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<OutboxPublicadorBackground> _logger;

    public OutboxPublicadorBackground(
        IServiceScopeFactory scopes,
        ILogger<OutboxPublicadorBackground> logger)
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
                using var scope = _scopes.CreateScope();
                if (!await IntentarObtenerLockAsync(scope.ServiceProvider, stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                var publicar = scope.ServiceProvider.GetRequiredService<PublicarOutboxPendientes>();
                var cantidad = await publicar.EjecutarAsync(50, stoppingToken);
                if (cantidad > 0)
                    _logger.LogInformation("Outbox: publicados {Cantidad} mensaje(s).", cantidad);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Outbox: error al publicar pendientes.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task<bool> IntentarObtenerLockAsync(
        IServiceProvider servicios,
        CancellationToken cancellationToken)
    {
        var redis = servicios.GetService<IConnectionMultiplexer>();
        if (redis is null)
        {
            // Sin Redis (dev local con caché en memoria): una sola instancia típica; se permite.
            return true;
        }

        try
        {
            var db = redis.GetDatabase();
            var token = Environment.MachineName + ":" + Environment.ProcessId;
            return await db.StringSetAsync(ClaveLock, token, TtlLock, When.NotExists);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Outbox: no se pudo tomar el lock Redis; se omite este ciclo.");
            return false;
        }
    }
}
