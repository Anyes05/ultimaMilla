using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

[Authorize(Roles = PermisosPerfil.Administracion)]
public class ComerciosModel : PageModel
{
    private readonly GestionComercios _gestion;
    private readonly IOperadorRepositorio _operadores;

    public ComerciosModel(GestionComercios gestion, IOperadorRepositorio operadores)
    {
        _gestion = gestion;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<Comercio> Lista { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    [BindProperty]
    public NuevoComercioForm Nuevo { get; set; } = new();

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;
        await CargarAsync();
    }

    public async Task<IActionResult> OnPostRegistrarAsync()
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
            await _gestion.RegistrarAsync(
                acceso.OperadorId,
                Nuevo.NombreComercial,
                Nuevo.RazonSocial ?? "",
                Nuevo.DocumentoFiscal ?? "",
                Nuevo.DireccionPrincipal ?? "");
            TempData["Mensaje"] = "Comercio registrado.";
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = ex.Message;
            await CargarAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool activo)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            await _gestion.CambiarActivoAsync(acceso.OperadorId, id, activo);
            TempData["Mensaje"] = activo ? "Comercio activado." : "Comercio desactivado.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        Operador = await _operadores.ObtenerAsync(acceso.OperadorId);
        Lista = await _gestion.ListarAsync(acceso.OperadorId);
    }

    public sealed class NuevoComercioForm
    {
        [Required, StringLength(120)]
        public string NombreComercial { get; set; } = string.Empty;

        [StringLength(160)]
        public string? RazonSocial { get; set; }

        [StringLength(40)]
        public string? DocumentoFiscal { get; set; }

        [StringLength(200)]
        public string? DireccionPrincipal { get; set; }
    }
}
