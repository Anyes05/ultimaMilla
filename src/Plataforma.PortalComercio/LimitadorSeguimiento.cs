using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace Plataforma.PortalComercio;

/// <summary>
/// Límite de consultas por IP del seguimiento público, compartido entre réplicas (letra 6.3 y aclaración 3.1:
/// el limitador de ASP.NET Core cuenta en cada instancia, y con dos réplicas el límite real sería el doble).
///
/// La cuenta vive en Redis, con una ventana fija de un minuto por IP; INCR y EXPIRE corren en un script Lua,
/// así que es atómico entre réplicas. Si Redis no está o no responde, se usa un limitador local por réplica
/// durante <see cref="Pausa"/>: el seguimiento sigue protegido, con un límite menos exacto.
/// </summary>
public sealed class LimitadorSeguimiento : IDisposable
{
    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Pausa = TimeSpan.FromSeconds(15);

    private const string Script = """
        local n = redis.call('INCR', KEYS[1])
        if n == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
        return n
        """;

    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<LimitadorSeguimiento> _logger;
    private readonly int _permitidas;
    private readonly PartitionedRateLimiter<string> _local;
    private long _pausadoHastaTicks;

    public LimitadorSeguimiento(IConnectionMultiplexer? redis, ILogger<LimitadorSeguimiento> logger, int permitidas)
    {
        _redis = redis;
        _logger = logger;
        _permitidas = permitidas;
        _local = PartitionedRateLimiter.Create<string, string>(ip =>
            RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitidas,
                Window = Ventana,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    /// <summary>True si la IP todavía puede consultar en esta ventana.</summary>
    public async Task<bool> PermitirAsync(string ip)
    {
        if (_redis is not null && DateTime.UtcNow.Ticks >= Interlocked.Read(ref _pausadoHastaTicks))
        {
            try
            {
                var ventana = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (long)Ventana.TotalSeconds;
                var clave = $"limite:seguimiento:{ip}:{ventana}";
                var cuenta = (long)await _redis.GetDatabase().ScriptEvaluateAsync(
                    Script, [clave], [(long)Ventana.TotalSeconds + 5]);
                return cuenta <= _permitidas;
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                // Solo el primer error de cada pausa va al log.
                var hasta = DateTime.UtcNow.Add(Pausa).Ticks;
                if (Interlocked.Exchange(ref _pausadoHastaTicks, hasta) < DateTime.UtcNow.Ticks)
                    _logger.LogWarning(ex, "Límite del seguimiento sin Redis: se cuenta por réplica durante {Segundos} s.",
                        Pausa.TotalSeconds);
            }
        }

        using var permiso = _local.AttemptAcquire(ip);
        return permiso.IsAcquired;
    }

    public void Dispose() => _local.Dispose();
}
