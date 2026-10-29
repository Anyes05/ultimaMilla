using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Avisos;

namespace Plataforma.Worker;

/// <summary>
/// Red de seguridad: vuelve a encolar entregas Pendientes que quedaron sin mensaje en la cola
/// (el worker se cayó entre guardar y publicar, o falló la publicación de un reintento manual).
/// Solo toma las que llevan más tiempo quietas que la espera más larga, así no adelanta reintentos en curso.
/// Si dos réplicas encolan la misma, la toma con concurrencia optimista evita el aviso duplicado.
/// </summary>
public sealed class BarridoEntregasPendientes : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly IPublicadorAvisos _publicador;
    private readonly OpcionesAvisos _avisos;
    private readonly ILogger<BarridoEntregasPendientes> _logger;

    public BarridoEntregasPendientes(
        IServiceScopeFactory scopes,
        IPublicadorAvisos publicador,
        IOptions<OpcionesAvisos> avisos,
        ILogger<BarridoEntregasPendientes> logger)
    {
        _scopes = scopes;
        _publicador = publicador;
        _avisos = avisos.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var quietas = TimeSpan.FromSeconds(_avisos.Esperas.Max() + 60);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Intervalo, stoppingToken);
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AvisosDbContext>();
                var limite = DateTime.UtcNow - quietas;
                var tomaVencida = DateTime.UtcNow - EntregaAviso.TomaVencida;

                var olvidadas = await db.Entregas.AsNoTracking()
                    .Where(e => e.Estado == EstadoEntrega.Pendiente &&
                                e.ActualizadaEn < limite &&
                                (e.EnCursoDesde == null || e.EnCursoDesde < tomaVencida))
                    .OrderBy(e => e.ActualizadaEn)
                    .Select(e => e.Id)
                    .Take(100)
                    .ToListAsync(stoppingToken);

                foreach (var id in olvidadas)
                    await _publicador.PublicarEntregaAsync(id, stoppingToken);

                if (olvidadas.Count > 0)
                    _logger.LogInformation("Barrido: {Cantidad} entrega(s) pendiente(s) reencolada(s).", olvidadas.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Barrido de entregas pendientes falló; se reintenta en {Segundos}s.", Intervalo.TotalSeconds);
            }
        }
    }
}
