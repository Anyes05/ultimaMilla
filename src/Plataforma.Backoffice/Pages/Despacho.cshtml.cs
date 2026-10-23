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
    private readonly ListarRutasDelDia _listarRutas;
    private readonly DespacharRuta _despachar;
    private readonly IniciarRuta _iniciarRuta;
    private readonly IOperadorRepositorio _operadores;

    public DespachoModel(
        ListarEnvios listarEnvios,
        ListarRepartidoresActivos listarRepartidores,
        ListarRutasDelDia listarRutas,
        DespacharRuta despachar,
        IniciarRuta iniciarRuta,
        IOperadorRepositorio operadores)
    {
        _listarEnvios = listarEnvios;
        _listarRepartidores = listarRepartidores;
        _listarRutas = listarRutas;
        _despachar = despachar;
        _iniciarRuta = iniciarRuta;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<Envio> EnviosEnDeposito { get; private set; } = [];
    public IReadOnlyList<RepartidorVisible> Repartidores { get; private set; } = [];
    public IReadOnlyList<RutaDelDiaResumen> RutasDeHoy { get; private set; } = [];
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

    [BindProperty]
    public DateOnly Fecha { get; set; }

    [BindProperty]
    public List<Guid> EnvioIds { get; set; } = [];

    // Despachar e iniciar en un solo paso; desmarcado, la ruta queda esperando "Iniciar ruta".
    [BindProperty]
    public bool IniciarAlDespachar { get; set; } = true;

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

            if (IniciarAlDespachar)
                await EjecutarInicioAsync(ruta.Id, acceso.OperadorId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = ex.Message;
        }

        // PRG: recargar no vuelve a despachar.
        return RedirectToPage();
    }

    /// <summary>
    /// Sale la ruta: pasa sus envíos AsignadoARuta → EnTransito.
    /// La app del repartidor solo puede entregar envíos en tránsito.
    /// </summary>
    public async Task<IActionResult> OnPostIniciarRutaAsync(Guid rutaId)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            await EjecutarInicioAsync(rutaId, acceso.OperadorId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = ex.Message;
        }

        // PRG: recargar no vuelve a iniciar.
        return RedirectToPage();
    }

    private async Task EjecutarInicioAsync(Guid rutaId, Guid operadorId)
    {
        var resultado = await _iniciarRuta.EjecutarAsync(
            operadorId,
            rutaId,
            User.Identity?.Name,
            HttpContext.RequestAborted);

        TempData["Mensaje"] =
            $"{TempData.Peek("Mensaje")} Ruta iniciada: {resultado.Iniciados} envío(s) en tránsito.".Trim();
        if (resultado.Errores.Count > 0)
            TempData["Error"] = string.Join(" ", resultado.Errores);
    }

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId);
        if (Fecha == default)
            Fecha = Operador?.DiaActual() ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var envios = await _listarEnvios.Ejecutar(acceso, ct);
        // Despachables: en depósito o reprogramados, sin RutaId (DespacharRuta asigna AsignadoARuta).
        EnviosEnDeposito = envios
            .Where(envio =>
                envio.RutaId is null &&
                envio.Estado is EstadoEnvio.EnDeposito or EstadoEnvio.Reprogramado)
            .OrderBy(envio => envio.FechaCreacion)
            .ToList();
        Repartidores = await _listarRepartidores.EjecutarAsync(acceso.OperadorId, ct);
        RutasDeHoy = await _listarRutas.EjecutarAsync(acceso.OperadorId, Fecha, ct);
    }
}
