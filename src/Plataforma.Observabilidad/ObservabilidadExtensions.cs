using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

namespace Plataforma.Observabilidad;

/// <summary>
/// Registro estructurado (Serilog) y métricas y trazas (OpenTelemetry) con la misma forma en los tres procesos.
///
/// Destino: la variable estándar OTEL_EXPORTER_OTLP_ENDPOINT (por ejemplo http://lgtm:4317 en compose).
/// Sin esa variable no se exporta nada y los logs salen solo por consola: el proceso funciona igual.
/// El identificador de la traza (TraceId) acompaña a cada log, así un mismo cambio de estado se sigue
/// desde el request web, por RabbitMQ, hasta el worker y la llamada HTTP al comercio.
/// </summary>
public static class ObservabilidadExtensions
{
    /// <summary>Medidores propios ("Plataforma.Avisos", "Plataforma.Web", ...).</summary>
    public const string MedidoresPlataforma = "Plataforma.*";

    // RabbitMQ.Client 7 crea las trazas de publicación y consumo y propaga el contexto en los headers del mensaje.
    private static readonly string[] FuentesTrazas =
    [
        "Plataforma.*",
        "RabbitMQ.Client.Publisher",
        "RabbitMQ.Client.Subscriber",
        "Npgsql"
    ];

    public static IHostApplicationBuilder AddObservabilidad(
        this IHostApplicationBuilder builder,
        string servicio,
        Action<TracerProviderBuilder>? trazas = null,
        Action<MeterProviderBuilder>? metricas = null)
    {
        var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var exportar = !string.IsNullOrWhiteSpace(endpoint);
        var desarrollo = builder.Environment.IsDevelopment();
        // En Kubernetes el nombre del pod; con dos réplicas permite ver el tráfico de cada una.
        var instancia = Environment.MachineName;

        builder.Services.AddSerilog((_, configuracion) =>
        {
            configuracion
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                // Una sección "Serilog" en appsettings o variables de entorno puede ajustar niveles sin recompilar.
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Servicio", servicio)
                .Enrich.WithProperty("Instancia", instancia);

            // Local: legible. Contenedor: JSON por línea (kubectl logs, CloudWatch) con TraceId y SpanId.
            if (desarrollo)
                configuracion.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj} {TraceId}{NewLine}{Exception}");
            else
                configuracion.WriteTo.Console(new CompactJsonFormatter());

            if (exportar)
                configuracion.WriteTo.OpenTelemetry(opciones =>
                {
                    opciones.Endpoint = endpoint!;
                    opciones.Protocol = OtlpProtocol.Grpc;
                    opciones.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = servicio,
                        ["service.instance.id"] = instancia
                    };
                });
        });

        if (!exportar)
            return builder;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService(servicio, serviceInstanceId: instancia))
            .WithMetrics(medidor =>
            {
                medidor
                    .AddMeter(MedidoresPlataforma)
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter();
                metricas?.Invoke(medidor);
            })
            .WithTracing(traza =>
            {
                traza
                    .AddSource(FuentesTrazas)
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter();
                trazas?.Invoke(traza);
            });

        return builder;
    }
}
