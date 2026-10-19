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
    private readonly ITransicionEnvio _transicion;
    private readonly IRutaRepositorio _rutas;
    private readonly IOperadorRepositorio _operadores;

    public DespachoModel(
        ListarEnvios listarEnvios,
        ListarRepartidoresActivos listarRepartidores,
        DespacharRuta despachar,
        ITransicionEnvio transicion,
        IRutaRepositorio rutas,
        IOperadorRepositorio operadores)
    {
        _listarEnvios = listarEnvios;
        _listarRepartidores = listarRepartidores;
        _despachar = despachar;
        _transicion = transicion;
        _rutas = rutas;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<Envio> EnviosEnDeposito { get; private set; } = [];
    public IReadOnlyList<RepartidorVisible> Repartidores { get; private set; } = [];
    public IReadOnlyList<RutaDelDiaVista> RutasDeHoy { get; private set; } = [];
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
            {
                var envios = (await _listarEnvios.Ejecutar(acceso, HttpContext.RequestAborted))
                    .Where(envio => envio.RutaId == ruta.Id && envio.Estado == EstadoEnvio.AsignadoARuta)
                    .ToList();
                await IniciarEnviosAsync(envios, acceso.OperadorId);
            }
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

        var ct = HttpContext.RequestAborted;
        // ListarEnvios ya filtra por operador: una ruta de otro operador no trae envíos.
        var envios = (await _listarEnvios.Ejecutar(acceso, ct))
            .Where(envio => envio.RutaId == rutaId && envio.Estado == EstadoEnvio.AsignadoARuta)
            .ToList();

        if (envios.Count == 0)
        {
            TempData["Error"] = "La ruta no tiene envíos pendientes de salir.";
            return RedirectToPage();
        }

        TempData.Remove("Mensaje");
        await IniciarEnviosAsync(envios, acceso.OperadorId);

        // PRG: recargar no vuelve a iniciar.
        return RedirectToPage();
    }

    /// <summary>AsignadoARuta → EnTransito para cada envío; informa los que fallen sin frenar al resto.</summary>
    private async Task IniciarEnviosAsync(IReadOnlyList<Envio> envios, Guid operadorId)
    {
        var iniciados = 0;
        var errores = new List<string>();
        foreach (var envio in envios)
        {
            try
            {
                await _transicion.EjecutarAsync(envio.Id, operadorId, EstadoEnvio.EnTransito, User.Identity?.Name,
                    cancellationToken: HttpContext.RequestAborted);
                iniciados++;
            }
            catch (InvalidOperationException ex)
            {
                errores.Add($"{envio.NumeroSeguimiento}: {ex.Message}");
            }
        }

        if (iniciados > 0)
            TempData["Mensaje"] = $"{TempData.Peek("Mensaje")} Ruta iniciada: {iniciados} envío(s) en tránsito.".Trim();
        if (errores.Count > 0)
            TempData["Error"] = string.Join(" ", errores);
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

        // Mismo día UTC que descarga la app (GET /api/repartidor/rutas/hoy).
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var rutasDeHoy = new List<RutaDelDiaVista>();
        foreach (var repartidor in Repartidores)
        {
            foreach (var ruta in await _rutas.ObtenerDeHoyAsync(acceso.OperadorId, repartidor.Id, hoy, ct))
            {
                var enviosRuta = envios.Where(envio => envio.RutaId == ruta.Id).ToList();
                rutasDeHoy.Add(new RutaDelDiaVista(ruta.Id, repartidor.Nombre, enviosRuta));
            }
        }
        RutasDeHoy = rutasDeHoy;
    }
}

public sealed record RutaDelDiaVista(Guid RutaId, string Repartidor, IReadOnlyList<Envio> Envios)
{
    public int PendientesDeSalir => Envios.Count(envio => envio.Estado == EstadoEnvio.AsignadoARuta);
}
