using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

/// <summary>Drena outbox pendiente hacia RabbitMQ (reintentos si falló la publicación inmediata).</summary>
public sealed class OutboxPublicadorBackground : BackgroundService
{
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
}
