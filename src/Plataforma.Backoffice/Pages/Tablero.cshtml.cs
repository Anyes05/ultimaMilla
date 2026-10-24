using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Avisos;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>
/// Tablero de operación en vivo (letra 6.10): carga el estado actual y después se actualiza por SignalR
/// con los cambios de estado y los resultados de los avisos a comercios, solo del operador autenticado.
/// </summary>
[Authorize(Roles = Roles.Administrador + "," + Roles.Despachador + "," + Roles.OperarioDeposito)]
public class TableroModel : PageModel
{
    private readonly ListarEnvios _listarEnvios;
    private readonly IOperadorRepositorio _operadores;
    private readonly IServiceProvider _servicios;
    private readonly ILogger<TableroModel> _logger;

    public TableroModel(
        ListarEnvios listarEnvios,
        IOperadorRepositorio operadores,
        IServiceProvider servicios,
        ILogger<TableroModel> logger)
    {
        _listarEnvios = listarEnvios;
        _operadores = operadores;
        _servicios = servicios;
        _logger = logger;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<Envio> Envios { get; private set; } = [];
    public ResumenAvisos? Avisos { get; private set; }

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
}
