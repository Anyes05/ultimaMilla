using Microsoft.EntityFrameworkCore;
using Npgsql;
using Plataforma.Avisos;

namespace Plataforma.Worker;

/// <summary>
/// Cierre diario de la operación (letra sección 4): recalcula los indicadores de cumplimiento de cada operador para
/// los últimos días ya terminados, en la hora de cada operador, y los deja en "CierresDiarios".
/// Corre al arrancar y después cada hora. Con varias réplicas lo ejecuta una sola (bloqueo consultivo de
/// Postgres, <see cref="EjecucionUnica"/>), y la clave única (operador, día) impide duplicados aunque dos corran.
/// Recalcula varios días hacia atrás porque la app puede sincronizar entregas atrasadas al recuperar señal.
/// </summary>
public sealed class CierreDiarioOperacion : BackgroundService
{
    // Valores de los enums del núcleo (el worker no referencia el dominio). Una prueba del dominio los fija.
    internal const int Entregado = 4;
    internal const int NoEntregado = 5;
    internal const int Devuelto = 8;
    internal const int EntregadoParcialmente = 10;
    internal const int RetiroCompletado = 3;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CierreDiarioOperacion> _logger;
    private readonly TimeSpan _intervalo;
    private readonly TimeSpan _demoraInicial;
    private readonly int _diasARecalcular;

    public CierreDiarioOperacion(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<CierreDiarioOperacion> logger)
    {
        _scopes = scopes;
        _logger = logger;
        _intervalo = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("CierreDiario:IntervaloMinutos", 60)));
        _demoraInicial = TimeSpan.FromSeconds(Math.Max(0, configuration.GetValue("CierreDiario:DemoraInicialSegundos", 30)));
        _diasARecalcular = Math.Clamp(configuration.GetValue("CierreDiario:DiasARecalcular", 3), 1, 31);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_demoraInicial, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await EjecutarUnaVezAsync(stoppingToken);
                await Task.Delay(_intervalo, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task EjecutarUnaVezAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AvisosDbContext>();
            var filas = 0;
            var ejecutado = await EjecucionUnica.IntentarAsync(
                db,
                EjecucionUnica.CierreDiario,
                async token => filas = await db.Database.ExecuteSqlAsync(Consulta(_diasARecalcular), token),
                ct);

            if (ejecutado)
                _logger.LogInformation("Cierre diario: {Filas} cierre(s) de operador y día calculados ({Dias} días hacia atrás).", filas, _diasARecalcular);
            else
                _logger.LogDebug("Cierre diario: otra réplica lo está ejecutando.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // La tabla la crea la migración del núcleo; si el worker arrancó antes, se vuelve a intentar en la próxima vuelta.
            _logger.LogWarning("Cierre diario: todavía no existe la tabla de cierres; se reintenta en {Minutos} min.", _intervalo.TotalMinutes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Cierre diario: falló el cálculo; se reintenta en la próxima vuelta.");
        }
    }

    /// <summary>
    /// Una sola sentencia para todos los operadores y días: así el cierre es atómico y lo resuelve la base.
    /// "A tiempo" compara la primera entrega con el plazo de la regla vigente al alta (urgente si corresponde).
    /// </summary>
    internal static FormattableString Consulta(int dias) => $"""
        WITH dias AS (
            SELECT o."Id" AS operador,
                   d::date AS dia,
                   (d::timestamp AT TIME ZONE o."ZonaHoraria") AS desde,
                   ((d + interval '1 day')::timestamp AT TIME ZONE o."ZonaHoraria") AS hasta
            FROM public."Operadores" o
            CROSS JOIN LATERAL generate_series(
                (now() AT TIME ZONE o."ZonaHoraria")::date - {dias},
                (now() AT TIME ZONE o."ZonaHoraria")::date - 1,
                interval '1 day') AS d
        ),
        entregas AS (
            SELECT h."OperadorId" AS operador, h."EnvioId" AS envio, min(h."Fecha") AS fecha
            FROM public."HistorialEstados" h
            WHERE h."Estado" = {Entregado}
            GROUP BY h."OperadorId", h."EnvioId"
        ),
        entregas_con_plazo AS (
            SELECT x.operador, x.fecha,
                   x.fecha <= e."FechaCreacion" + make_interval(hours =>
                       CASE WHEN e."Urgente" AND r."PlazoEntregaUrgenteHoras" IS NOT NULL
                            THEN r."PlazoEntregaUrgenteHoras" ELSE r."PlazoEntregaHoras" END) AS a_tiempo
            FROM entregas x
            JOIN public."Envios" e ON e."Id" = x.envio
            JOIN LATERAL (
                SELECT regla."PlazoEntregaHoras", regla."PlazoEntregaUrgenteHoras"
                FROM public."ReglasOperativas" regla
                WHERE regla."OperadorId" = e."OperadorId" AND regla."Activa"
                  AND regla."VigenciaDesde" <= e."FechaCreacion" AND e."FechaCreacion" <= regla."VigenciaHasta"
                ORDER BY regla."VigenciaDesde" DESC
                LIMIT 1) r ON true
        )
        INSERT INTO public."CierresDiarios"
            ("Id", "OperadorId", "Dia", "EnviosAdmitidos", "Entregados", "EntregadosConPlazo", "EntregadosATiempo",
             "IntentosFallidos", "Devueltos", "RetirosCompletados", "CalculadoUtc")
        SELECT gen_random_uuid(), d.operador, d.dia,
            (SELECT count(*) FROM public."Envios" e
              WHERE e."OperadorId" = d.operador AND e."FechaCreacion" >= d.desde AND e."FechaCreacion" < d.hasta),
            (SELECT count(*) FROM entregas x
              WHERE x.operador = d.operador AND x.fecha >= d.desde AND x.fecha < d.hasta),
            (SELECT count(*) FROM entregas_con_plazo p
              WHERE p.operador = d.operador AND p.fecha >= d.desde AND p.fecha < d.hasta),
            (SELECT count(*) FROM entregas_con_plazo p
              WHERE p.operador = d.operador AND p.fecha >= d.desde AND p.fecha < d.hasta AND p.a_tiempo),
            (SELECT count(*) FROM public."HistorialEstados" h
              WHERE h."OperadorId" = d.operador AND h."Estado" IN ({NoEntregado}, {EntregadoParcialmente})
                AND h."Fecha" >= d.desde AND h."Fecha" < d.hasta),
            (SELECT count(*) FROM public."HistorialEstados" h
              WHERE h."OperadorId" = d.operador AND h."Estado" = {Devuelto}
                AND h."Fecha" >= d.desde AND h."Fecha" < d.hasta),
            (SELECT count(*) FROM public."SolicitudesRetiro" s
              WHERE s."OperadorId" = d.operador AND s."Estado" = {RetiroCompletado} AND s."FechaSolicitada" = d.dia),
            now()
        FROM dias d
        ON CONFLICT ("OperadorId", "Dia") DO UPDATE SET
            "EnviosAdmitidos" = EXCLUDED."EnviosAdmitidos",
            "Entregados" = EXCLUDED."Entregados",
            "EntregadosConPlazo" = EXCLUDED."EntregadosConPlazo",
            "EntregadosATiempo" = EXCLUDED."EntregadosATiempo",
            "IntentosFallidos" = EXCLUDED."IntentosFallidos",
            "Devueltos" = EXCLUDED."Devueltos",
            "RetirosCompletados" = EXCLUDED."RetirosCompletados",
            "CalculadoUtc" = EXCLUDED."CalculadoUtc"
        """;
}
