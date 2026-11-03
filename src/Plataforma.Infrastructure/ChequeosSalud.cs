using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Plataforma.Infrastructure;

/// <summary>
/// Puntos de verificación de salud de los hosts web (letra 6.13: /health, /ready y /live).
/// <list type="bullet">
/// <item><c>/live</c>: el proceso responde. No mira dependencias: si fallara por la base, Kubernetes reiniciaría
/// pods sanos en cadena.</item>
/// <item><c>/ready</c>: puede atender: la base responde. 503 si no, y el balanceador deja de mandarle tráfico.</item>
/// <item><c>/health</c>: estado completo en JSON (base, Redis, RabbitMQ). Redis o RabbitMQ caídos dan
/// "Degraded" y 200, porque el sistema sigue funcionando (caché contra la base, outbox con reintentos);
/// la base caída da "Unhealthy" y 503.</item>
/// </list>
/// </summary>
public static class ChequeosSalud
{
    private const string Listo = "listo";
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(3);

    public static IServiceCollection AddChequeosSalud(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<ChequeoBaseDeDatos>("base-de-datos", HealthStatus.Unhealthy, [Listo], Limite)
            .AddCheck<ChequeoRedis>("redis", HealthStatus.Degraded, timeout: Limite)
            .AddCheck<ChequeoRabbitMq>("rabbitmq", HealthStatus.Degraded, timeout: Limite);
        return services;
    }

    public static IEndpointRouteBuilder MapChequeosSalud(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = chequeo => chequeo.Tags.Contains(Listo) })
            .AllowAnonymous();
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            ResponseWriter = EscribirJsonAsync
        }).AllowAnonymous();
        return app;
    }

    /// <summary>Rutas de sonda: sin trazas ni log por request.</summary>
    public static bool EsSonda(PathString ruta) =>
        ruta.StartsWithSegments("/live") || ruta.StartsWithSegments("/ready") || ruta.StartsWithSegments("/health");

    // Sin datos internos (ni cadenas de conexión ni mensajes de excepción): el endpoint es anónimo.
    private static Task EscribirJsonAsync(HttpContext contexto, HealthReport reporte)
    {
        contexto.Response.ContentType = "application/json; charset=utf-8";
        var cuerpo = new
        {
            estado = reporte.Status.ToString(),
            duracionMs = (int)reporte.TotalDuration.TotalMilliseconds,
            chequeos = reporte.Entries.ToDictionary(
                entrada => entrada.Key,
                entrada => new
                {
                    estado = entrada.Value.Status.ToString(),
                    detalle = entrada.Value.Description,
                    duracionMs = (int)entrada.Value.Duration.TotalMilliseconds
                })
        };
        return contexto.Response.WriteAsync(JsonSerializer.Serialize(cuerpo), contexto.RequestAborted);
    }

    private sealed class ChequeoBaseDeDatos(PlataformaDbContext db) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                return await db.Database.CanConnectAsync(cancellationToken)
                    ? HealthCheckResult.Healthy("PostgreSQL responde.")
                    : HealthCheckResult.Unhealthy("PostgreSQL no responde.");
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Unhealthy("PostgreSQL no responde.");
            }
        }
    }

    private sealed class ChequeoRedis(IServiceProvider servicios) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var redis = servicios.GetService<IConnectionMultiplexer>();
            if (redis is null)
                return HealthCheckResult.Healthy("Sin Redis configurado: caché en memoria.");

            try
            {
                var latencia = await redis.GetDatabase().PingAsync();
                return HealthCheckResult.Healthy($"Redis responde en {latencia.TotalMilliseconds:0} ms.");
            }
            catch (Exception)
            {
                return HealthCheckResult.Degraded("Redis no responde: el caché lee de la base.");
            }
        }
    }

    // Alcanza con abrir el puerto: una conexión AMQP completa por cada consulta de salud sería costosa.
    private sealed class ChequeoRabbitMq(IOptions<OpcionesRabbitMq> opciones) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var rabbit = opciones.Value;
            if (string.IsNullOrWhiteSpace(rabbit.Host))
                return HealthCheckResult.Healthy("Sin RabbitMQ configurado: el outbox queda pendiente.");

            var reloj = Stopwatch.StartNew();
            try
            {
                using var cliente = new TcpClient();
                await cliente.ConnectAsync(rabbit.Host, rabbit.Port, cancellationToken);
                return HealthCheckResult.Healthy($"RabbitMQ acepta conexiones ({reloj.ElapsedMilliseconds} ms).");
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Degraded("RabbitMQ no responde: los eventos quedan en el outbox y se reintentan.");
            }
        }
    }
}
