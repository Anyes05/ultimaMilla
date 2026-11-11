using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>Reportes de gestión y liquidación por comercio (RF 30).</summary>
[Authorize(Roles = PermisosPerfil.Administracion)]
public class ReportesModel : PageModel
{
    private readonly ObtenerReportesGestion _reportes;
    private readonly GestionLiquidaciones _liquidaciones;
    private readonly GestionComercios _comercios;
    private readonly IOperadorRepositorio _operadores;
    private readonly ListarCierresDiarios _cierres;

    /// <summary>Cierres diarios del worker en el período (indicadores de cumplimiento por día).</summary>
    public IReadOnlyList<CierreDiario> Cierres { get; private set; } = [];

    public ReportesModel(
        ListarCierresDiarios cierres,
        ObtenerReportesGestion reportes,
        GestionLiquidaciones liquidaciones,
        GestionComercios comercios,
        IOperadorRepositorio operadores)
    {
        _reportes = reportes;
        _liquidaciones = liquidaciones;
        _comercios = comercios;
        _operadores = operadores;
        _cierres = cierres;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Desde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Hasta { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ComercioId { get; set; }

    public Operador? Operador { get; private set; }
    public ReportesGestion? Datos { get; private set; }
    public IReadOnlyList<Comercio> Comercios { get; private set; } = [];
    public IReadOnlyList<Liquidacion> Liquidaciones { get; private set; } = [];
    public string? Mensaje { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;

        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Comercios = (await _comercios.ListarAsync(acceso.OperadorId, ct)).OrderBy(comercio => comercio.NombreComercial).ToList();
        var hoy = Operador?.DiaActual() ?? DateOnly.FromDateTime(DateTime.UtcNow);
        Desde ??= new DateOnly(hoy.Year, hoy.Month, 1);
        Hasta ??= hoy;

        try
        {
            Datos = await _reportes.EjecutarAsync(acceso.OperadorId, Desde.Value, Hasta.Value, ComercioId, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = TextosUsuario.Error(ex);
        }

        Cierres = await _cierres.EjecutarAsync(acceso.OperadorId, Desde.Value, Hasta.Value, ct);

        Liquidaciones = (await _liquidaciones.ListarAsync(acceso.OperadorId, ComercioId, ct))
            .OrderByDescending(liquidacion => liquidacion.PeriodoHasta)
            .ToList();
    }

    /// <summary>Abre la liquidación del comercio por los envíos entregados del período elegido.</summary>
    public async Task<IActionResult> OnPostAbrirAsync(Guid comercioLiquidar)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            var ct = HttpContext.RequestAborted;
            var hoy = (await _operadores.ObtenerAsync(acceso.OperadorId, ct))?.DiaActual() ?? DateOnly.FromDateTime(DateTime.UtcNow);
            // Sin período en la dirección, el mismo que muestra la página: el mes en curso.
            Desde ??= new DateOnly(hoy.Year, hoy.Month, 1);
            Hasta ??= hoy;
            if (Desde is not DateOnly desde || Hasta is not DateOnly hasta || hasta < desde)
                throw new ArgumentException("Elegí un período válido.");
            if ((await _comercios.ListarAsync(acceso.OperadorId, ct)).All(comercio => comercio.Id != comercioLiquidar))
                throw new InvalidOperationException("El comercio no existe para ese operador.");

            var operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct)
                ?? throw new InvalidOperationException("El operador no existe.");
            var zona = ObtenerReportesGestion.Zona(operador);
            await _liquidaciones.AbrirAsync(
                acceso.OperadorId,
                comercioLiquidar,
                ObtenerReportesGestion.AUtc(desde.ToDateTime(TimeOnly.MinValue), zona),
                ObtenerReportesGestion.AUtc(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), zona).AddTicks(-1),
                ct);
            TempData["Mensaje"] = "Liquidación abierta.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        return RedirectToPage(new { Desde, Hasta, ComercioId });
    }

    public async Task<IActionResult> OnPostCerrarAsync(Guid liquidacionId)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            await _liquidaciones.CerrarAsync(acceso.OperadorId, liquidacionId, HttpContext.RequestAborted);
            TempData["Mensaje"] = "Liquidación cerrada.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        return RedirectToPage(new { Desde, Hasta, ComercioId });
    }

    public string NombreComercio(Guid comercioId) =>
        Comercios.FirstOrDefault(comercio => comercio.Id == comercioId)?.NombreComercial ?? "—";

    /// <summary>Fecha local del operador de un instante guardado en UTC.</summary>
    public string FechaLocal(DateTime utc) =>
        Operador is null
            ? utc.ToString("dd/MM/yyyy")
            : ObtenerReportesGestion.ALocal(utc, ObtenerReportesGestion.Zona(Operador)).ToString("dd/MM/yyyy");

    public string FechaHoraLocal(DateTime utc) =>
        Operador is null
            ? utc.ToString("dd/MM HH:mm")
            : ObtenerReportesGestion.ALocal(utc, ObtenerReportesGestion.Zona(Operador)).ToString("dd/MM HH:mm");

    public static string Importe(decimal valor) =>
        valor.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("es-UY"));

    public static string Porcentaje(decimal? valor) =>
        valor is decimal v ? $"{v:0.#} %" : "—";
}
