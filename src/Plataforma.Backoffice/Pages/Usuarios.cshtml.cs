using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>Usuarios del operador y de sus comercios: alta, modificación, baja lógica y perfil (RF 1).</summary>
[Authorize(Roles = PermisosPerfil.Administracion)]
public class UsuariosModel : PageModel
{
    public static readonly IReadOnlyList<SelectListItem> Perfiles =
    [
        new("Administrador", nameof(RolUsuario.Administrador)),
        new("Despachador", nameof(RolUsuario.Despachador)),
        new("Operario de depósito", nameof(RolUsuario.OperarioDeposito)),
        new("Comercio", nameof(RolUsuario.Comercio))
    ];

    private readonly GestionUsuarios _gestion;
    private readonly GestionComercios _comercios;
    private readonly IOperadorRepositorio _operadores;

    public UsuariosModel(GestionUsuarios gestion, GestionComercios comercios, IOperadorRepositorio operadores)
    {
        _gestion = gestion;
        _comercios = comercios;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<UsuarioPlataforma> Usuarios { get; private set; } = [];
    public IReadOnlyList<Comercio> Comercios { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    /// <summary>Usuario que se está editando; sin valor, el formulario da de alta.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Editar { get; set; }

    [BindProperty]
    public UsuarioForm Formulario { get; set; } = new();

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;
        await CargarAsync();

        if (Editar is Guid id && Usuarios.FirstOrDefault(usuario => usuario.Id == id) is { } editado)
        {
            Formulario = new UsuarioForm
            {
                Nombre = editado.Nombre,
                Email = editado.Email,
                NombreUsuario = editado.NombreUsuario,
                Rol = editado.Rol,
                ComercioId = editado.ComercioId
            };
        }
    }

    public async Task<IActionResult> OnPostGuardarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        if (Editar is null && string.IsNullOrWhiteSpace(Formulario.NombreUsuario))
            ModelState.AddModelError("Formulario.NombreUsuario", "El nombre de usuario es requerido.");
        if (Editar is null && (Formulario.Clave?.Length ?? 0) < 6)
            ModelState.AddModelError("Formulario.Clave", "La clave debe tener al menos 6 caracteres.");
        if (Formulario.Rol == RolUsuario.Comercio && Formulario.ComercioId is null)
            ModelState.AddModelError("Formulario.ComercioId", "Elegí el comercio del usuario.");

        if (!ModelState.IsValid)
        {
            await CargarAsync();
            return Page();
        }

        var comercioId = Formulario.Rol == RolUsuario.Comercio ? Formulario.ComercioId : null;
        try
        {
            if (Editar is Guid id)
            {
                await _gestion.ActualizarAsync(
                    acceso.OperadorId, id, Formulario.Nombre, Formulario.Email, Formulario.Rol, comercioId, HttpContext.RequestAborted);
                TempData["Mensaje"] = "Usuario actualizado.";
            }
            else
            {
                await _gestion.RegistrarAsync(
                    acceso.OperadorId, Formulario.Nombre, Formulario.Email, Formulario.NombreUsuario!, Formulario.Clave!,
                    Formulario.Rol, comercioId, HttpContext.RequestAborted);
                TempData["Mensaje"] = "Usuario creado.";
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = TextosUsuario.Error(ex);
            await CargarAsync();
            return Page();
        }

        // PRG: recargar no vuelve a guardar.
        return RedirectToPage(new { Editar = (Guid?)null });
    }

    public async Task<IActionResult> OnPostCambiarActivoAsync(Guid usuarioId, bool activo)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        if (!activo && usuarioId == acceso.UsuarioId)
        {
            TempData["Error"] = "No podés desactivar tu propio usuario.";
            return RedirectToPage(new { Editar = (Guid?)null });
        }

        try
        {
            await _gestion.CambiarActivoAsync(acceso.OperadorId, usuarioId, activo, HttpContext.RequestAborted);
            TempData["Mensaje"] = activo ? "Usuario reactivado." : "Usuario desactivado: ya no puede iniciar sesión.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        return RedirectToPage(new { Editar = (Guid?)null });
    }

    public static string NombrePerfil(RolUsuario rol) =>
        Perfiles.FirstOrDefault(perfil => perfil.Value == rol.ToString())?.Text ?? rol.ToString();

    public string NombreComercio(Guid? comercioId) =>
        Comercios.FirstOrDefault(comercio => comercio.Id == comercioId)?.NombreComercial ?? "—";

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Comercios = await _comercios.ListarAsync(acceso.OperadorId, ct);
        // Repartidores y administradores de plataforma no se gestionan acá.
        Usuarios = (await _gestion.ListarAsync(acceso.OperadorId, ct))
            .Where(usuario => Perfiles.Any(perfil => perfil.Value == usuario.Rol.ToString()))
            .OrderBy(usuario => usuario.NombreUsuario)
            .ToList();
    }

    public sealed class UsuarioForm
    {
        [Required(ErrorMessage = "El nombre es requerido.")]
        [StringLength(120, ErrorMessage = "El nombre es demasiado largo.")]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "El email es requerido.")]
        [EmailAddress(ErrorMessage = "El email no es válido.")]
        [StringLength(160, ErrorMessage = "El email es demasiado largo.")]
        public string Email { get; set; } = string.Empty;

        [StringLength(80, ErrorMessage = "El nombre de usuario es demasiado largo.")]
        public string? NombreUsuario { get; set; }

        [StringLength(80, ErrorMessage = "La clave es demasiado larga.")]
        public string? Clave { get; set; }

        public RolUsuario Rol { get; set; } = RolUsuario.Despachador;

        public Guid? ComercioId { get; set; }
    }
}
