using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>
/// Cuadro tarifario del operador (RF 3, RNF 6.6).
/// Hoy: tarifa vigente + simulador con CalcularTarifa.
/// Pendiente de Ana: listar versiones y publicar una nueva versión (formulario e historial deshabilitados).
/// </summary>
[Authorize(Roles = Roles.Administrador)]
public class TarifasModel : PageModel
{
    private readonly ITarifaRepositorio _tarifas;
    private readonly CalcularTarifa _calcular;
    private readonly IOperadorRepositorio _operadores;

    public TarifasModel(ITarifaRepositorio tarifas, CalcularTarifa calcular, IOperadorRepositorio operadores)
    {
        _tarifas = tarifas;
        _calcular = calcular;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public ReglaTarifaria? Vigente { get; private set; }
    public decimal? ImporteSimulado { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Se habilita cuando exista el caso de uso de versionado (PublicarVersionAsync / ListarVersionesAsync).</summary>
    public bool VersionadoDisponible => false;

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    [BindProperty]
    public SimulacionForm Simulacion { get; set; } = new();

    public async Task OnGetAsync() => await CargarAsync();

    public async Task<IActionResult> OnPostSimularAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        await CargarAsync();
        if (!ModelState.IsValid)
            return Page();

        try
        {
            // Mismo cálculo que el alta del Portal: lo que se ve acá es lo que cobra un envío nuevo.
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

    public static string Importe(decimal valor) =>
        valor.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("es-UY"));

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Vigente = await _tarifas.ObtenerActivaAsync(acceso.OperadorId, DateTime.UtcNow, ct);
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
}
