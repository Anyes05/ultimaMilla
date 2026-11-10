using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Cumplimiento de un grupo (zona, repartidor o comercio) en el período.</summary>
public sealed record FilaCumplimiento(
    string Nombre,
    int Envios,
    int Finalizados,
    int Entregados,
    int Devueltos,
    int ConPlazo,
    int ATiempo)
{
    /// <summary>Entregados sobre los envíos que ya terminaron (entregados, devueltos o extraviados).</summary>
    public decimal? PorcentajeEntrega => Finalizados == 0 ? null : Math.Round(100m * Entregados / Finalizados, 1);

    /// <summary>Entregados dentro del plazo comprometido, sobre los entregados que tenían plazo.</summary>
    public decimal? PorcentajeATiempo => ConPlazo == 0 ? null : Math.Round(100m * ATiempo / ConPlazo, 1);
}

public sealed record VolumenDia(DateOnly Dia, int Envios, decimal Monto);

public sealed record CantidadPorMotivo(string Motivo, int Cantidad);

public sealed record LiquidacionComercio(Guid ComercioId, string Comercio, int Entregados, decimal Monto);

public sealed record ResumenRetiros(int Solicitados, int Programados, int EnCurso, int Completados, int Cancelados);

/// <summary>Reportes de gestión (RF 30, y retiros por el cambio de requerimientos) para un período del operador.</summary>
public sealed record ReportesGestion(
    DateOnly Desde,
    DateOnly Hasta,
    int Envios,
    int Entregados,
    int Devueltos,
    int EnCurso,
    decimal MontoEntregado,
    FilaCumplimiento Total,
    IReadOnlyList<FilaCumplimiento> PorZona,
    IReadOnlyList<FilaCumplimiento> PorRepartidor,
    IReadOnlyList<FilaCumplimiento> PorComercio,
    IReadOnlyList<CantidadPorMotivo> MotivosNoEntrega,
    IReadOnlyList<VolumenDia> Volumen,
    IReadOnlyList<LiquidacionComercio> Liquidacion,
    ResumenRetiros Retiros);

/// <summary>
/// Arma los reportes del operador a partir de los envíos creados en el período (fechas locales del operador).
/// "A tiempo" compara la primera entrega del historial con el plazo de la regla operativa vigente al alta.
/// </summary>
public sealed class ObtenerReportesGestion
{
    private readonly IEnvioRepositorio _envios;
    private readonly IHistorialEstadoRepositorio _historial;
    private readonly IReglaOperativaRepositorio _reglas;
    private readonly IRutaRepositorio _rutas;
    private readonly IRepartidorRepositorio _repartidores;
    private readonly ICoberturaRepositorio _cobertura;
    private readonly IComercioRepositorio _comercios;
    private readonly IOperadorRepositorio _operadores;
    private readonly GestionRetiros _retiros;

    public ObtenerReportesGestion(
        IEnvioRepositorio envios,
        IHistorialEstadoRepositorio historial,
        IReglaOperativaRepositorio reglas,
        IRutaRepositorio rutas,
        IRepartidorRepositorio repartidores,
        ICoberturaRepositorio cobertura,
        IComercioRepositorio comercios,
        IOperadorRepositorio operadores,
        GestionRetiros retiros)
    {
        _envios = envios;
        _historial = historial;
        _reglas = reglas;
        _rutas = rutas;
        _repartidores = repartidores;
        _cobertura = cobertura;
        _comercios = comercios;
        _operadores = operadores;
        _retiros = retiros;
    }

    public async Task<ReportesGestion> EjecutarAsync(
        Guid operadorId,
        DateOnly desde,
        DateOnly hasta,
        Guid? comercioId = null,
        CancellationToken cancellationToken = default)
    {
        if (hasta < desde)
            throw new ArgumentException("La fecha final no puede ser anterior a la inicial.");
        if (hasta.DayNumber - desde.DayNumber > 366)
            throw new ArgumentException("El período puede abarcar como máximo un año.");

        var operador = await _operadores.ObtenerAsync(operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El operador no existe.");
        var zona = Zona(operador);
        var inicioUtc = AUtc(desde.ToDateTime(TimeOnly.MinValue), zona);
        var finUtc = AUtc(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), zona);

        var envios = (await _envios.ListarAsync(operadorId, comercioId, cancellationToken))
            .Where(envio => envio.FechaCreacion >= inicioUtc && envio.FechaCreacion < finUtc)
            .ToList();

        var historial = (await _historial.ListarPorEnviosAsync(operadorId, envios.Select(envio => envio.Id).ToList(), cancellationToken))
            .GroupBy(evento => evento.EnvioId)
            .ToDictionary(grupo => grupo.Key, grupo => (IReadOnlyList<HistorialEstado>)grupo.OrderBy(evento => evento.Fecha).ToList());
        var reglas = await _reglas.ListarVersionesAsync(operadorId, cancellationToken);

        var zonas = (await _cobertura.ListarZonasAsync(operadorId, cancellationToken)).ToDictionary(z => z.Id, z => z.Nombre);
        var comercios = (await _comercios.ListarAsync(operadorId, cancellationToken)).ToDictionary(c => c.Id, c => c.NombreComercial);
        var repartidores = (await _repartidores.ListarAsync(operadorId, cancellationToken)).ToDictionary(r => r.Id, r => r.Nombre);
        var repartidorPorRuta = new Dictionary<Guid, string>();
        foreach (var rutaId in envios.Where(e => e.RutaId is not null).Select(e => e.RutaId!.Value).Distinct())
        {
            var ruta = await _rutas.ObtenerAsync(operadorId, rutaId, cancellationToken);
            repartidorPorRuta[rutaId] = ruta is not null && repartidores.TryGetValue(ruta.RepartidorId, out var nombre) ? nombre : "Repartidor sin datos";
        }

        var filas = envios.Select(envio =>
        {
            var eventos = historial.TryGetValue(envio.Id, out var lista) ? lista : [];
            var entregaUtc = eventos.FirstOrDefault(evento => evento.Estado == EstadoEnvio.Entregado)?.Fecha;
            var regla = ReglaVigente(reglas, envio.FechaCreacion);
            var plazo = regla is null ? (DateTime?)null : envio.FechaCreacion.AddHours(regla.PlazoEntregaHoras);
            return new Fila(
                envio,
                envio.ZonaId is Guid z && zonas.TryGetValue(z, out var nombreZona) ? nombreZona : "Sin zona",
                envio.RutaId is Guid r ? repartidorPorRuta[r] : "Sin ruta",
                comercios.TryGetValue(envio.ComercioId, out var nombreComercio) ? nombreComercio : "Comercio sin datos",
                entregaUtc,
                plazo);
        }).ToList();

        var motivos = historial.Values
            .SelectMany(eventos => eventos)
            .Where(evento => evento.Estado == EstadoEnvio.NoEntregado)
            .GroupBy(evento => string.IsNullOrWhiteSpace(evento.Motivo) ? "Sin motivo informado" : evento.Motivo.Trim())
            .Select(grupo => new CantidadPorMotivo(grupo.Key, grupo.Count()))
            .OrderByDescending(motivo => motivo.Cantidad)
            .ThenBy(motivo => motivo.Motivo)
            .ToList();

        var volumen = filas
            .GroupBy(fila => DateOnly.FromDateTime(ALocal(fila.Envio.FechaCreacion, zona)))
            .Select(grupo => new VolumenDia(grupo.Key, grupo.Count(), grupo.Sum(fila => fila.Envio.TarifaCalculada)))
            .OrderBy(dia => dia.Dia)
            .ToList();

        var liquidacion = filas
            .Where(fila => fila.Envio.Estado == EstadoEnvio.Entregado)
            .GroupBy(fila => (fila.Envio.ComercioId, fila.Comercio))
            .Select(grupo => new LiquidacionComercio(grupo.Key.ComercioId, grupo.Key.Comercio, grupo.Count(), grupo.Sum(fila => fila.Envio.TarifaCalculada)))
            .OrderByDescending(item => item.Monto)
            .ToList();

        var retiros = (await _retiros.ListarAsync(operadorId, comercioId, cancellationToken))
            .Where(retiro => retiro.FechaSolicitada >= desde && retiro.FechaSolicitada <= hasta)
            .ToList();

        var entregados = envios.Count(envio => envio.Estado == EstadoEnvio.Entregado);
        var devueltos = envios.Count(envio => envio.Estado == EstadoEnvio.Devuelto);
        return new ReportesGestion(
            desde,
            hasta,
            envios.Count,
            entregados,
            devueltos,
            envios.Count(envio => !Finalizado(envio.Estado)),
            filas.Where(fila => fila.Envio.Estado == EstadoEnvio.Entregado).Sum(fila => fila.Envio.TarifaCalculada),
            Agrupar("Total", filas),
            Agrupado(filas, fila => fila.Zona),
            Agrupado(filas, fila => fila.Repartidor),
            Agrupado(filas, fila => fila.Comercio),
            motivos,
            volumen,
            liquidacion,
            new ResumenRetiros(
                retiros.Count(retiro => retiro.Estado == EstadoRetiro.Solicitado),
                retiros.Count(retiro => retiro.Estado == EstadoRetiro.Programado),
                retiros.Count(retiro => retiro.Estado == EstadoRetiro.EnCurso),
                retiros.Count(retiro => retiro.Estado == EstadoRetiro.Completado),
                retiros.Count(retiro => retiro.Estado == EstadoRetiro.Cancelado)));
    }

    /// <summary>Misma condición que el repositorio: activa y con el instante dentro de su vigencia; gana la más reciente.</summary>
    internal static ReglaOperativa? ReglaVigente(IEnumerable<ReglaOperativa> versiones, DateTime instanteUtc) =>
        versiones
            .Where(regla => regla.Activa && regla.VigenciaDesde <= instanteUtc && instanteUtc <= regla.VigenciaHasta)
            .OrderByDescending(regla => regla.VigenciaDesde)
            .FirstOrDefault();

    public static TimeZoneInfo Zona(Operador operador)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(operador.ZonaHoraria);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static DateTime AUtc(DateTime local, TimeZoneInfo zona) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zona);

    public static DateTime ALocal(DateTime utc, TimeZoneInfo zona) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zona);

    private static bool Finalizado(EstadoEnvio estado) =>
        estado is EstadoEnvio.Entregado or EstadoEnvio.Devuelto or EstadoEnvio.Extraviado;

    private static IReadOnlyList<FilaCumplimiento> Agrupado(IEnumerable<Fila> filas, Func<Fila, string> clave) =>
        filas.GroupBy(clave)
            .Select(grupo => Agrupar(grupo.Key, grupo))
            .OrderByDescending(fila => fila.Envios)
            .ThenBy(fila => fila.Nombre)
            .ToList();

    private static FilaCumplimiento Agrupar(string nombre, IEnumerable<Fila> filas)
    {
        var lista = filas.ToList();
        var entregadosConPlazo = lista.Where(fila => fila.EntregaUtc is not null && fila.PlazoUtc is not null).ToList();
        return new FilaCumplimiento(
            nombre,
            lista.Count,
            lista.Count(fila => Finalizado(fila.Envio.Estado)),
            lista.Count(fila => fila.Envio.Estado == EstadoEnvio.Entregado),
            lista.Count(fila => fila.Envio.Estado == EstadoEnvio.Devuelto),
            entregadosConPlazo.Count,
            entregadosConPlazo.Count(fila => fila.EntregaUtc <= fila.PlazoUtc));
    }

    private sealed record Fila(Envio Envio, string Zona, string Repartidor, string Comercio, DateTime? EntregaUtc, DateTime? PlazoUtc);
}

/// <summary>Envío activo cuyo plazo comprometido vence pronto o ya venció (RF 29).</summary>
public sealed record EnvioEnRiesgo(
    Guid EnvioId,
    string NumeroSeguimiento,
    string Destinatario,
    EstadoEnvio Estado,
    DateTime CompromisoUtc,
    string Criterio);

public sealed record ResultadoEnRiesgo(int Total, IReadOnlyList<EnvioEnRiesgo> Envios);

/// <summary>
/// Envíos en riesgo de incumplir su ventana (RF 29). El compromiso es el plazo de la regla vigente al alta, contado
/// desde el alta o desde la última reprogramación; si el envío tiene franja y ruta, el fin de esa franja el día
/// de la ruta, si es antes. Está en riesgo si al compromiso le queda menos que el margen, o si ya venció.
/// </summary>
public sealed class ListarEnviosEnRiesgo
{
    public static readonly TimeSpan MargenPorDefecto = TimeSpan.FromHours(2);

    private static readonly EstadoEnvio[] Activos =
    [
        EstadoEnvio.Admitido, EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito,
        EstadoEnvio.EntregadoParcialmente, EstadoEnvio.NoEntregado, EstadoEnvio.Reprogramado
    ];

    private readonly IEnvioRepositorio _envios;
    private readonly IHistorialEstadoRepositorio _historial;
    private readonly IReglaOperativaRepositorio _reglas;
    private readonly IRutaRepositorio _rutas;
    private readonly ICoberturaRepositorio _cobertura;
    private readonly IOperadorRepositorio _operadores;

    public ListarEnviosEnRiesgo(
        IEnvioRepositorio envios,
        IHistorialEstadoRepositorio historial,
        IReglaOperativaRepositorio reglas,
        IRutaRepositorio rutas,
        ICoberturaRepositorio cobertura,
        IOperadorRepositorio operadores)
    {
        _envios = envios;
        _historial = historial;
        _reglas = reglas;
        _rutas = rutas;
        _cobertura = cobertura;
        _operadores = operadores;
    }

    public async Task<ResultadoEnRiesgo> EjecutarAsync(
        Guid operadorId,
        DateTime ahoraUtc,
        TimeSpan margen,
        int maximo,
        CancellationToken cancellationToken = default)
    {
        var operador = await _operadores.ObtenerAsync(operadorId, cancellationToken);
        if (operador is null)
            return new ResultadoEnRiesgo(0, []);

        var activos = (await _envios.ListarAsync(operadorId, null, cancellationToken))
            .Where(envio => Activos.Contains(envio.Estado))
            .ToList();
        if (activos.Count == 0)
            return new ResultadoEnRiesgo(0, []);

        var zona = ObtenerReportesGestion.Zona(operador);
        var reglas = await _reglas.ListarVersionesAsync(operadorId, cancellationToken);
        var franjas = (await _cobertura.ListarTodasLasFranjasAsync(operadorId, cancellationToken)).ToDictionary(f => f.Id);
        var reprogramados = activos.Where(envio => envio.Estado == EstadoEnvio.Reprogramado).Select(envio => envio.Id).ToList();
        var ultimaReprogramacion = (await _historial.ListarPorEnviosAsync(operadorId, reprogramados, cancellationToken))
            .Where(evento => evento.Estado == EstadoEnvio.Reprogramado)
            .GroupBy(evento => evento.EnvioId)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Max(evento => evento.Fecha));
        var fechaRuta = new Dictionary<Guid, DateOnly?>();

        var enRiesgo = new List<EnvioEnRiesgo>();
        foreach (var envio in activos)
        {
            DateTime? compromiso = null;
            var criterio = string.Empty;

            if (ObtenerReportesGestion.ReglaVigente(reglas, envio.FechaCreacion) is { } regla)
            {
                var desde = ultimaReprogramacion.TryGetValue(envio.Id, out var reprogramado) ? reprogramado : envio.FechaCreacion;
                compromiso = desde.AddHours(regla.PlazoEntregaHoras);
                criterio = $"Plazo de {regla.PlazoEntregaHoras} h";
            }

            if (envio.FranjaId is Guid franjaId && franjas.TryGetValue(franjaId, out var franja) && envio.RutaId is Guid rutaId)
            {
                if (!fechaRuta.TryGetValue(rutaId, out var dia))
                {
                    dia = (await _rutas.ObtenerAsync(operadorId, rutaId, cancellationToken))?.Fecha;
                    fechaRuta[rutaId] = dia;
                }

                if (dia is DateOnly fecha)
                {
                    var finFranja = ObtenerReportesGestion.AUtc(fecha.ToDateTime(franja.Hasta), zona);
                    if (compromiso is null || finFranja < compromiso)
                    {
                        compromiso = finFranja;
                        criterio = $"Franja {franja.Nombre}";
                    }
                }
            }

            if (compromiso is DateTime vence && vence - ahoraUtc <= margen)
                enRiesgo.Add(new EnvioEnRiesgo(envio.Id, envio.NumeroSeguimiento, envio.Destinatario, envio.Estado, vence, criterio));
        }

        return new ResultadoEnRiesgo(
            enRiesgo.Count,
            enRiesgo.OrderBy(envio => envio.CompromisoUtc).Take(maximo).ToList());
    }
}
