using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>
/// Cuadro tarifario del operador: vigente, simulador, publicar versión e historial.
/// </summary>
[Authorize(Roles = Roles.Administrador)]
public class TarifasModel : PageModel
{
    private readonly ITarifaRepositorio _tarifas;
    private readonly CalcularTarifa _calcular;
    private readonly GestionTarifas _gestion;
    private readonly IOperadorRepositorio _operadores;

    public TarifasModel(
        ITarifaRepositorio tarifas,
        CalcularTarifa calcular,
        GestionTarifas gestion,
        IOperadorRepositorio operadores)
    {
        _tarifas = tarifas;
        _calcular = calcular;
        _gestion = gestion;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public ReglaTarifaria? Vigente { get; private set; }
    public IReadOnlyList<TarifaVersionVista> Versiones { get; private set; } = [];
    public decimal? ImporteSimulado { get; private set; }
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    public bool VersionadoDisponible => true;

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    [BindProperty]
    public SimulacionForm Simulacion { get; set; } = new();

    [BindProperty]
    public NuevaVersionForm NuevaVersion { get; set; } = new();

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;
        await CargarAsync();
    }

    public async Task<IActionResult> OnPostSimularAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        await CargarAsync();
        if (!ModelState.IsValid)
            return Page();

        try
        {
            ImporteSimulado = await _calcular.EjecutarAsync(
                acceso.OperadorId,
                Simulacion.PesoKg,
                Simulacion.DistanciaKm,
                Simulacion.Fragil,
                Simulacion.Urgente,
                HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostPublicarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        if (!ModelState.IsValid)
        {
            await CargarAsync();
            return Page();
        }

        try
        {
            var desde = NuevaVersion.VigenteDesdeLocal == default
                ? DateTime.UtcNow
                : DateTime.SpecifyKind(NuevaVersion.VigenteDesdeLocal, DateTimeKind.Local).ToUniversalTime();

            await _gestion.PublicarVersionAsync(
                acceso.OperadorId,
                new NuevaVersionTarifa(
                    NuevaVersion.PrecioBase,
                    NuevaVersion.PrecioPorKg,
                    NuevaVersion.PrecioPorKm,
                    NuevaVersion.RecargoFragil,
                    NuevaVersion.RecargoUrgente),
                desde,
                User.Identity?.Name ?? acceso.UsuarioId.ToString(),
                HttpContext.RequestAborted);

            TempData["Mensaje"] = "Nueva versión de tarifa publicada.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public static string Importe(decimal valor) =>
        valor.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("es-UY"));

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Vigente = await _tarifas.ObtenerActivaAsync(acceso.OperadorId, DateTime.UtcNow, ct);
        Versiones = await _gestion.ListarVersionesAsync(acceso.OperadorId, ct);

        if (NuevaVersion.PrecioBase == 0 && Vigente is not null)
        {
            NuevaVersion.PrecioBase = Vigente.PrecioBase;
            NuevaVersion.PrecioPorKg = Vigente.PrecioPorKg;
            NuevaVersion.PrecioPorKm = Vigente.PrecioPorKm;
            NuevaVersion.RecargoFragil = Vigente.RecargoFragil;
            NuevaVersion.RecargoUrgente = Vigente.RecargoUrgente;
        }
    }

    public sealed class SimulacionForm
    {
        [Range(typeof(decimal), "0", "1000", ErrorMessage = "El peso debe estar entre 0 y 1000 kg.")]
        public decimal PesoKg { get; set; } = 1;

        [Range(typeof(decimal), "0", "500", ErrorMessage = "La distancia debe estar entre 0 y 500 km.")]
        public decimal DistanciaKm { get; set; } = 5;

        public bool Urgente { get; set; }

        public bool Fragil { get; set; }
    }

    public sealed class NuevaVersionForm
    {
        [Range(typeof(decimal), "0", "1000000")]
        public decimal PrecioBase { get; set; }

        [Range(typeof(decimal), "0", "1000000")]
        public decimal PrecioPorKg { get; set; }

        [Range(typeof(decimal), "0", "1000000")]
        public decimal PrecioPorKm { get; set; }

        [Range(typeof(decimal), "0", "1000000")]
        public decimal RecargoFragil { get; set; }

        [Range(typeof(decimal), "0", "1000000")]
        public decimal RecargoUrgente { get; set; }

        public DateTime VigenteDesdeLocal { get; set; } = DateTime.Now;
    }
}
