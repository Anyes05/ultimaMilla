using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

[Authorize(Roles = PermisosPerfil.Administracion)]
public class UsuariosModel : PageModel
{
    private readonly GestionUsuarios _gestion;
    private readonly IOperadorRepositorio _operadores;

    public UsuariosModel(GestionUsuarios gestion, IOperadorRepositorio operadores)
    {
        _gestion = gestion;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<UsuarioPlataforma> Lista { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    public IEnumerable<SelectListItem> RolesDisponibles { get; } =
    [
        new(nameof(RolUsuario.Administrador), nameof(RolUsuario.Administrador)),
        new(nameof(RolUsuario.Despachador), nameof(RolUsuario.Despachador)),
        new(nameof(RolUsuario.OperarioDeposito), nameof(RolUsuario.OperarioDeposito)),
        new(nameof(RolUsuario.Comercio), nameof(RolUsuario.Comercio))
    ];

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    [BindProperty]
    public NuevoUsuarioForm Nuevo { get; set; } = new();

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
            if (!Enum.TryParse<RolUsuario>(Nuevo.Rol, ignoreCase: true, out var rol))
            {
                Error = "Rol inválido.";
                await CargarAsync();
                return Page();
            }

            await _gestion.RegistrarAsync(
                acceso.OperadorId,
                Nuevo.Nombre,
                Nuevo.Email,
                Nuevo.NombreUsuario,
                Nuevo.Clave,
                rol,
                Nuevo.ComercioId);
            TempData["Mensaje"] = "Usuario registrado.";
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
            TempData["Mensaje"] = activo ? "Usuario activado." : "Usuario desactivado.";
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

    public sealed class NuevoUsuarioForm
    {
        [Required, StringLength(120)]
        public string Nombre { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(160)]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(80)]
        public string NombreUsuario { get; set; } = string.Empty;

        [Required, StringLength(80, MinimumLength = 6)]
        public string Clave { get; set; } = string.Empty;

        [Required]
        public string Rol { get; set; } = nameof(RolUsuario.Despachador);

        public Guid? ComercioId { get; set; }
    }
}
