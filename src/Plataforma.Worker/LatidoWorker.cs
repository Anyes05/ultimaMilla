using System.Collections.Concurrent;

namespace Plataforma.Worker;

/// <summary>
/// Salud del worker para Kubernetes (letra 6.13). El worker no expone HTTP, así que las sondas son de tipo exec
/// y leen la antigüedad de dos archivos:
/// <list type="bullet">
/// <item><c>/tmp/worker-vivo</c>: se actualiza cada <see cref="Intervalo"/> mientras el proceso funciona (liveness).</item>
/// <item><c>/tmp/worker-listo</c>: se actualiza solo si todos los consumidores escuchan su cola (readiness);
/// se borra si alguno perdió RabbitMQ.</item>
/// </list>
/// Sonda sugerida: <c>find /tmp/worker-listo -mmin -1 | grep -q .</c> (falla si el archivo no existe o tiene más de 1 min).
/// </summary>
public sealed class LatidoWorker : BackgroundService
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(15);

    private static readonly ConcurrentDictionary<string, bool> Escuchando = new();

    private readonly string _vivo;
    private readonly string _listo;
    private readonly ILogger<LatidoWorker> _logger;

    public LatidoWorker(ILogger<LatidoWorker> logger, IConfiguration configuracion)
    {
        _logger = logger;
        var carpeta = configuracion["Salud:Carpeta"] ?? Path.GetTempPath();
        _vivo = Path.Combine(carpeta, "worker-vivo");
        _listo = Path.Combine(carpeta, "worker-listo");
    }

    /// <summary>Lo informa cada consumidor al empezar a escuchar su cola y al perder la conexión.</summary>
    public static void Informar(string consumidor, bool escuchando) => Escuchando[consumidor] = escuchando;

    /// <summary>Listo cuando hay consumidores registrados y todos escuchan.</summary>
    public static bool TodosEscuchando => !Escuchando.IsEmpty && Escuchando.Values.All(escucha => escucha);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await File.WriteAllTextAsync(_vivo, DateTime.UtcNow.ToString("O"), stoppingToken);
                if (TodosEscuchando)
                    await File.WriteAllTextAsync(_listo, DateTime.UtcNow.ToString("O"), stoppingToken);
                else
                    File.Delete(_listo);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo escribir el latido de salud del worker.");
            }

            await Task.Delay(Intervalo, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Apagado controlado: deja de estar listo antes de terminar.
        File.Delete(_listo);
        await base.StopAsync(cancellationToken);
    }
}
