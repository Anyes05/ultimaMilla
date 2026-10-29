using Plataforma.Avisos;
using Plataforma.Observabilidad;
using Plataforma.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Logs estructurados, métricas y trazas (letra 6.12). La traza que llega en los headers del mensaje continúa acá.
builder.AddObservabilidad("plataforma-worker");
builder.Services.AddAvisos(builder.Configuration);
builder.Services.AddHttpClient(ConsumidorEntregas.ClienteHttp);
builder.Services.AddHostedService<ConsumidorEnvioEstadoCambiado>();
builder.Services.AddHostedService<ConsumidorEntregas>();
builder.Services.AddHostedService<BarridoEntregasPendientes>();
builder.Services.AddHostedService<MonitorColas>();

// Apagado controlado: da tiempo a terminar el mensaje en curso antes de cortar (SIGTERM de Kubernetes).
builder.Services.Configure<HostOptions>(opciones => opciones.ShutdownTimeout = TimeSpan.FromSeconds(20));

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Default")))
{
    logger.LogCritical("Falta ConnectionStrings:Default: el worker necesita la base para la idempotencia y el historial.");
    return 1;
}

// La base puede tardar en estar disponible (compose o pod nuevo): se reintenta antes de consumir.
for (var intento = 1; ; intento++)
{
    try
    {
        await host.Services.MigrarAvisosAsync();
        logger.LogInformation("Esquema {Esquema} migrado.", AvisosDbContext.Esquema);
        break;
    }
    catch (Exception ex) when (intento < 12)
    {
        logger.LogWarning(ex, "No se pudo migrar el esquema de avisos (intento {Intento}); reintento en 5 s.", intento);
        await Task.Delay(TimeSpan.FromSeconds(5));
    }
}

await host.RunAsync();
return 0;

public partial class Program;
