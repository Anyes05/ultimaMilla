using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>Repartidores del operador y su acceso a la app móvil (RF 6).</summary>
[Authorize(Roles = PermisosPerfil.Administracion)]
public class RepartidoresModel : PageModel
{
    private readonly GestionRepartidores _gestion;
    private readonly GestionUsuarios _usuarios;
    private readonly IOperadorRepositorio _operadores;

    public RepartidoresModel(GestionRepartidores gestion, GestionUsuarios usuarios, IOperadorRepositorio operadores)
    {
        _gestion = gestion;
        _usuarios = usuarios;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<Repartidor> Repartidores { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> NombresUsuario { get; private set; } = new Dictionary<Guid, string>();
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    [BindProperty(SupportsGet = true)]
    public Guid? Editar { get; set; }

    [BindProperty]
    public RepartidorForm Formulario { get; set; } = new();

    /// <summary>El repartidor en edición ya tiene acceso a la app: no se le pide otro.</summary>
    public bool TieneAcceso => Repartidores.Any(repartidor => repartidor.Id == Editar && repartidor.UsuarioId is not null);

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;
        await CargarAsync();

        if (Repartidores.FirstOrDefault(repartidor => repartidor.Id == Editar) is { } editado)
        {
            Formulario = new RepartidorForm
            {
                Nombre = editado.Nombre,
                Documento = editado.Documento,
                Telefono = editado.Telefono
            };
        }
    }

    public async Task<IActionResult> OnPostGuardarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        var ct = HttpContext.RequestAborted;
        await CargarAsync();

        var existente = Repartidores.FirstOrDefault(repartidor => repartidor.Id == Editar);
        if (Editar is not null && existente is null)
            Error = "El repartidor no existe para ese operador.";

        var creaAcceso = !string.IsNullOrWhiteSpace(Formulario.NombreUsuario) && existente?.UsuarioId is null;
        if (creaAcceso && string.IsNullOrWhiteSpace(Formulario.Email))
            ModelState.AddModelError("Formulario.Email", "El email es requerido para dar acceso a la app.");
        if (creaAcceso && (Formulario.Clave?.Length ?? 0) < 6)
            ModelState.AddModelError("Formulario.Clave", "La clave debe tener al menos 6 caracteres.");

        if (!ModelState.IsValid || Error is not null)
            return Page();

        try
        {
            var usuarioId = existente?.UsuarioId;
            if (creaAcceso)
            {
                var usuario = await _usuarios.RegistrarAsync(
                    acceso.OperadorId, Formulario.Nombre, Formulario.Email!, Formulario.NombreUsuario!, Formulario.Clave!,
                    RolUsuario.Repartidor, cancellationToken: ct);
                usuarioId = usuario.Id;
            }

            if (existente is not null)
                await _gestion.ActualizarAsync(
                    acceso.OperadorId, existente.Id, Formulario.Nombre, Formulario.Documento ?? string.Empty,
                    Formulario.Telefono ?? string.Empty, usuarioId, ct);
            else
                await _gestion.RegistrarAsync(
                    acceso.OperadorId, Formulario.Nombre, Formulario.Documento ?? string.Empty,
                    Formulario.Telefono ?? string.Empty, usuarioId, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = TextosUsuario.Error(ex);
            return Page();
        }

        TempData["Mensaje"] = existente is null ? "Repartidor creado." : "Repartidor actualizado.";
        return RedirectToPage(new { Editar = (Guid?)null });
    }

    public async Task<IActionResult> OnPostCambiarActivoAsync(Guid repartidorId, bool activo)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            await _gestion.CambiarActivoAsync(acceso.OperadorId, repartidorId, activo, HttpContext.RequestAborted);
            TempData["Mensaje"] = activo ? "Repartidor reactivado." : "Repartidor desactivado: no puede usar la app ni recibir rutas.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        return RedirectToPage(new { Editar = (Guid?)null });
    }

    public string NombreUsuario(Guid? usuarioId) =>
        usuarioId is Guid id && NombresUsuario.TryGetValue(id, out var nombre) ? nombre : "Sin acceso";

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Repartidores = (await _gestion.ListarAsync(acceso.OperadorId, soloActivos: false, ct))
            .OrderBy(repartidor => repartidor.Nombre)
            .ToList();
        NombresUsuario = (await _usuarios.ListarAsync(acceso.OperadorId, ct))
            .ToDictionary(usuario => usuario.Id, usuario => usuario.NombreUsuario);
    }

    public sealed class RepartidorForm
    {
        [Required(ErrorMessage = "El nombre es requerido.")]
        [StringLength(120, ErrorMessage = "El nombre es demasiado largo.")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(40, ErrorMessage = "El documento es demasiado largo.")]
        public string? Documento { get; set; }

        [StringLength(40, ErrorMessage = "El teléfono es demasiado largo.")]
        public string? Telefono { get; set; }

        [StringLength(80, ErrorMessage = "El usuario es demasiado largo.")]
        public string? NombreUsuario { get; set; }

        [EmailAddress(ErrorMessage = "El email no es válido.")]
        [StringLength(160, ErrorMessage = "El email es demasiado largo.")]
        public string? Email { get; set; }

        [StringLength(80, ErrorMessage = "La clave es demasiado larga.")]
        public string? Clave { get; set; }
    }
}
