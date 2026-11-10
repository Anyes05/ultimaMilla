using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>Comercios del operador: alta, modificación, baja lógica y consulta (RF 1).</summary>
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
    public IReadOnlyList<Comercio> Comercios { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    /// <summary>Comercio que se está editando; sin valor, el formulario da de alta.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Editar { get; set; }

    [BindProperty]
    public ComercioForm Formulario { get; set; } = new();

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;
        await CargarAsync();

        if (Editar is Guid id && Comercios.FirstOrDefault(comercio => comercio.Id == id) is { } editado)
        {
            Formulario = new ComercioForm
            {
                NombreComercial = editado.NombreComercial,
                RazonSocial = editado.RazonSocial,
                DocumentoFiscal = editado.DocumentoFiscal,
                DireccionPrincipal = editado.DireccionPrincipal
            };
        }
    }

    public async Task<IActionResult> OnPostGuardarAsync()
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
            if (Editar is Guid id)
            {
                await _gestion.ActualizarAsync(
                    acceso.OperadorId, id, Formulario.NombreComercial, Formulario.RazonSocial,
                    Formulario.DocumentoFiscal, Formulario.DireccionPrincipal, HttpContext.RequestAborted);
                TempData["Mensaje"] = "Comercio actualizado.";
            }
            else
            {
                await _gestion.RegistrarAsync(
                    acceso.OperadorId, Formulario.NombreComercial, Formulario.RazonSocial,
                    Formulario.DocumentoFiscal, Formulario.DireccionPrincipal, HttpContext.RequestAborted);
                TempData["Mensaje"] = "Comercio creado.";
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

    public async Task<IActionResult> OnPostCambiarActivoAsync(Guid comercioId, bool activo)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            await _gestion.CambiarActivoAsync(acceso.OperadorId, comercioId, activo, HttpContext.RequestAborted);
            TempData["Mensaje"] = activo
                ? "Comercio reactivado."
                : "Comercio dado de baja: sus usuarios ya no pueden entrar ni crear envíos.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        return RedirectToPage(new { Editar = (Guid?)null });
    }

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Comercios = await _gestion.ListarAsync(acceso.OperadorId, ct);
    }

    public sealed class ComercioForm
    {
        [Required(ErrorMessage = "El nombre comercial es requerido.")]
        [StringLength(200, ErrorMessage = "El nombre comercial es demasiado largo.")]
        public string NombreComercial { get; set; } = string.Empty;

        [Required(ErrorMessage = "La razón social es requerida.")]
        [StringLength(200, ErrorMessage = "La razón social es demasiado larga.")]
        public string RazonSocial { get; set; } = string.Empty;

        [Required(ErrorMessage = "El documento fiscal es requerido.")]
        [StringLength(30, ErrorMessage = "El documento fiscal es demasiado largo.")]
        public string DocumentoFiscal { get; set; } = string.Empty;

        [Required(ErrorMessage = "La dirección es requerida.")]
        [StringLength(300, ErrorMessage = "La dirección es demasiado larga.")]
        public string DireccionPrincipal { get; set; } = string.Empty;
    }
}
