using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>
/// Arma y despacha una ruta: elige repartidor, fecha y envíos en depósito.
/// Usa los casos de uso de Application directamente (sin HTTP interno).
/// </summary>
[Authorize(Roles = Roles.Administrador + "," + Roles.Despachador + "," + Roles.OperarioDeposito)]
public class DespachoModel : PageModel
{
    private readonly ListarEnvios _listarEnvios;
    private readonly ListarRepartidoresActivos _listarRepartidores;
    private readonly DespacharRuta _despachar;
    private readonly IOperadorRepositorio _operadores;

    public DespachoModel(
        ListarEnvios listarEnvios,
        ListarRepartidoresActivos listarRepartidores,
        DespacharRuta despachar,
        IOperadorRepositorio operadores)
    {
        _listarEnvios = listarEnvios;
        _listarRepartidores = listarRepartidores;
        _despachar = despachar;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<Envio> EnviosEnDeposito { get; private set; } = [];
    public IReadOnlyList<RepartidorVisible> Repartidores { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    [BindProperty]
    public Guid RepartidorId { get; set; }

    // Misma referencia de día que usa GET /api/repartidor/rutas/hoy (UTC).
    [BindProperty]
    public DateOnly Fecha { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [BindProperty]
    public List<Guid> EnvioIds { get; set; } = [];

    public async Task OnGetAsync()
    {
        Error = TempData["Error"] as string;
        Mensaje = TempData["Mensaje"] as string;
        await CargarAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        if (RepartidorId == Guid.Empty)
        {
            TempData["Error"] = "Elegí un repartidor.";
            return RedirectToPage();
        }

        if (EnvioIds.Count == 0)
        {
            TempData["Error"] = "Seleccioná al menos un envío para la ruta.";
            return RedirectToPage();
        }

        try
        {
            var ruta = await _despachar.EjecutarAsync(
                acceso.OperadorId,
                RepartidorId,
                Fecha,
                EnvioIds.Distinct().ToList(),
                HttpContext.RequestAborted);

            TempData["Mensaje"] =
                $"Ruta despachada para el {ruta.Fecha:dd/MM/yyyy} con {EnvioIds.Distinct().Count()} envío(s).";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = ex.Message;
        }

        // PRG: recargar no vuelve a despachar.
        return RedirectToPage();
    }

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        var envios = await _listarEnvios.Ejecutar(acceso, ct);
        // Solo los que DespacharRuta puede asignar: en depósito y sin ruta previa.
        EnviosEnDeposito = envios
            .Where(envio => envio.Estado == EstadoEnvio.EnDeposito && envio.RutaId is null)
            .OrderBy(envio => envio.FechaCreacion)
            .ToList();
        Repartidores = await _listarRepartidores.EjecutarAsync(acceso.OperadorId, ct);
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId);
    }
}
