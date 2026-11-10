using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Avisos;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>
/// Tablero de operación en vivo (letra 6.10): carga el estado actual y después se actualiza por SignalR
/// con los cambios de estado y los resultados de los avisos a comercios, solo del operador autenticado.
/// </summary>
[Authorize(Roles = PermisosPerfil.Planificacion)]
public class TableroModel : PageModel
{
    private readonly ListarEnvios _listarEnvios;
    private readonly IOperadorRepositorio _operadores;
    private readonly IServiceProvider _servicios;
    private readonly ILogger<TableroModel> _logger;
    private readonly IBultoRepositorio _bultos;
    private readonly GestionRetiros _retiros;
    private readonly GestionComercios _comercios;

    private const int FilasDetalle = 20;

    public TableroModel(
        IBultoRepositorio bultos,
        GestionRetiros retiros,
        GestionComercios comercios,
        ListarEnvios listarEnvios,
        IOperadorRepositorio operadores,
        IServiceProvider servicios,
        ILogger<TableroModel> logger)
    {
        _listarEnvios = listarEnvios;
        _operadores = operadores;
        _servicios = servicios;
        _logger = logger;
        _bultos = bultos;
        _retiros = retiros;
        _comercios = comercios;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<Envio> Envios { get; private set; } = [];
    public ResumenAvisos? Avisos { get; private set; }
    public IReadOnlyList<ParcialTablero> Parciales { get; private set; } = [];
    public int TotalParciales { get; private set; }
    public IReadOnlyList<SolicitudRetiro> RetirosAbiertos { get; private set; } = [];
    public int TotalRetirosAbiertos { get; private set; }
    public int RetirosSolicitados { get; private set; }
    public int RetirosProgramados { get; private set; }
    public int RetirosEnCurso { get; private set; }
    public int RetirosCompletadosHoy { get; private set; }
    public IReadOnlyDictionary<Guid, string> Comercios { get; private set; } = new Dictionary<Guid, string>();

    public IEnumerable<(string Estado, int Cantidad)> ConteoPorEstado =>
        Enum.GetValues<EstadoEnvio>().Select(estado => (estado.ToString(), Envios.Count(envio => envio.Estado == estado)));

    public async Task OnGetAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Envios = (await _listarEnvios.Ejecutar(acceso, ct))
            .OrderByDescending(envio => envio.FechaCreacion)
            .Take(200)
            .ToList();
        await CargarDetalleAsync(acceso, ct);

        try
        {
            var avisos = _servicios.GetService<ServicioAvisos>();
            if (avisos is not null)
                Avisos = await avisos.ResumenOperadorAsync(acceso.OperadorId, ct);
        }
        catch (Exception ex) when (ex is Npgsql.PostgresException or InvalidOperationException)
        {
            // El tablero de envíos funciona aunque el módulo de avisos todavía no esté migrado.
            _logger.LogWarning(ex, "Tablero: no se pudo leer el resumen de avisos.");
        }
    }

    /// <summary>Solo el detalle de parciales y retiros: lo pide la pantalla al llegar un evento.</summary>
    public async Task<IActionResult> OnGetDetalleAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        await CargarDetalleAsync(acceso, HttpContext.RequestAborted);
        return Partial("_TableroDetalle", this);
    }

    private async Task CargarDetalleAsync(AccesoActual acceso, CancellationToken ct)
    {
        Operador ??= await _operadores.ObtenerAsync(acceso.OperadorId, ct);

        var parciales = await _listarEnvios.EjecutarPaginadoAsync(acceso, EstadoEnvio.EntregadoParcialmente, 1, FilasDetalle, ct);
        TotalParciales = parciales.Total;
        var filas = new List<ParcialTablero>(parciales.Items.Count);
        foreach (var envio in parciales.Items)
        {
            var bultos = await _bultos.ListarPorEnvioAsync(acceso.OperadorId, envio.Id, ct);
            filas.Add(new ParcialTablero(
                envio,
                bultos.Count(bulto => bulto.Estado == EstadoBulto.Entregado),
                bultos.Count,
                bultos.Where(bulto => bulto.Estado != EstadoBulto.Entregado).ToList()));
        }
        Parciales = filas;

        var retiros = await _retiros.ListarAsync(acceso.OperadorId, cancellationToken: ct);
        var hoy = Operador?.DiaActual() ?? DateOnly.FromDateTime(DateTime.UtcNow);
        RetirosSolicitados = retiros.Count(retiro => retiro.Estado == EstadoRetiro.Solicitado);
        RetirosProgramados = retiros.Count(retiro => retiro.Estado == EstadoRetiro.Programado);
        RetirosEnCurso = retiros.Count(retiro => retiro.Estado == EstadoRetiro.EnCurso);
        RetirosCompletadosHoy = retiros.Count(retiro => retiro.Estado == EstadoRetiro.Completado && retiro.FechaSolicitada == hoy);
        var abiertos = retiros.Where(RetirosModel.EstaAbierto).OrderBy(retiro => retiro.FechaSolicitada).ToList();
        TotalRetirosAbiertos = abiertos.Count;
        RetirosAbiertos = abiertos.Take(FilasDetalle).ToList();
        Comercios = (await _comercios.ListarAsync(acceso.OperadorId, ct))
            .ToDictionary(comercio => comercio.Id, comercio => comercio.NombreComercial);
    }

    public sealed record ParcialTablero(Envio Envio, int Entregados, int Total, IReadOnlyList<Bulto> Faltantes);
}
