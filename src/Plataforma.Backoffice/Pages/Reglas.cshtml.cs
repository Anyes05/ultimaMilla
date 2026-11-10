using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>Reglas operativas del operador: vigente, publicar versión e historial (RF 4, letra 6.6).</summary>
[Authorize(Roles = PermisosPerfil.Administracion)]
public class ReglasModel : PageModel
{
    public static readonly IReadOnlyList<SelectListItem> Politicas =
    [
        new("Nueva visita sin recargo", nameof(PoliticaBultosPendientes.NuevaVisitaSinRecargo)),
        new("Nueva visita con recargo", nameof(PoliticaBultosPendientes.NuevaVisitaConRecargo)),
        new("Devolución al comercio", nameof(PoliticaBultosPendientes.DevolucionAlComercio))
    ];

    private readonly GestionReglasOperativas _gestion;
    private readonly IOperadorRepositorio _operadores;

    public ReglasModel(GestionReglasOperativas gestion, IOperadorRepositorio operadores)
    {
        _gestion = gestion;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<ReglaOperativa> Versiones { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    [BindProperty]
    public NuevaVersionForm Nueva { get; set; } = new();

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;
        await CargarAsync();
    }

    public async Task<IActionResult> OnPostAsync()
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
            await _gestion.PublicarAsync(
                acceso.OperadorId,
                new NuevaVersionRegla(
                    Nueva.MaximoIntentos,
                    Nueva.PlazoEntregaHoras,
                    Nueva.PlazoReprogramacionHoras,
                    Nueva.ExigeFirma,
                    Nueva.ExigeFoto,
                    Nueva.AdmiteEntregaParcial,
                    Nueva.PoliticaBultosPendientes,
                    Nueva.MotivosNoEntrega.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                HttpContext.RequestAborted);

            TempData["Mensaje"] = "Nueva versión de las reglas publicada. Ya está vigente.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        // PRG: recargar no vuelve a publicar.
        return RedirectToPage();
    }

    public static bool EsVigente(ReglaOperativa regla)
    {
        var ahora = DateTime.UtcNow;
        return regla.VigenciaDesde <= ahora && ahora <= regla.VigenciaHasta;
    }

    public static string NombrePolitica(PoliticaBultosPendientes politica) =>
        Politicas.First(item => item.Value == politica.ToString()).Text;

    public string HoraOperador(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById(Operador?.ZonaHoraria ?? Operador.ZonaHorariaUruguay)).ToString("dd/MM/yyyy HH:mm");

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Versiones = await _gestion.ListarVersionesAsync(acceso.OperadorId, ct);

        // El formulario arranca con los valores vigentes; con un error de validación conserva lo que se escribió.
        if (Nueva.MaximoIntentos == 0 && await _gestion.ObtenerVigenteAsync(acceso.OperadorId, ct) is { } vigente)
        {
            Nueva = new NuevaVersionForm
            {
                MaximoIntentos = vigente.MaximoIntentos,
                PlazoEntregaHoras = vigente.PlazoEntregaHoras,
                PlazoReprogramacionHoras = vigente.PlazoReprogramacionHoras,
                ExigeFirma = vigente.ExigeFirma,
                ExigeFoto = vigente.ExigeFoto,
                AdmiteEntregaParcial = vigente.AdmiteEntregaParcial,
                PoliticaBultosPendientes = vigente.PoliticaBultosPendientes,
                MotivosNoEntrega = string.Join(Environment.NewLine, vigente.MotivosNoEntrega)
            };
        }
    }

    public sealed class NuevaVersionForm
    {
        [Range(1, 10, ErrorMessage = "El máximo de intentos debe estar entre 1 y 10.")]
        public int MaximoIntentos { get; set; }

        [Range(1, 720, ErrorMessage = "El plazo de entrega debe estar entre 1 y 720 horas.")]
        public int PlazoEntregaHoras { get; set; }

        [Range(0, 720, ErrorMessage = "El plazo de reprogramación debe estar entre 0 y 720 horas.")]
        public int PlazoReprogramacionHoras { get; set; }

        public bool ExigeFirma { get; set; }

        public bool ExigeFoto { get; set; }

        public bool AdmiteEntregaParcial { get; set; }

        public PoliticaBultosPendientes PoliticaBultosPendientes { get; set; }

        [StringLength(1000, ErrorMessage = "El catálogo de motivos no puede superar 1000 caracteres.")]
        public string MotivosNoEntrega { get; set; } = string.Join(
            Environment.NewLine, ReglaOperativa.MotivosNoEntregaPredeterminados);
    }
}
