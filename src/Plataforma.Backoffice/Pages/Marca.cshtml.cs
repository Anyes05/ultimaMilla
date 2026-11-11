using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>Identidad visual del operador (RF 5): color y datos de contacto, que se ven en el Portal, el seguimiento y los correos.</summary>
[Authorize(Roles = PermisosPerfil.Administracion)]
public class MarcaModel : PageModel
{
    public static readonly IReadOnlyList<SelectListItem> Colores =
    [
        new("Violeta", "violeta"),
        new("Azul", "azul"),
        new("Verde", "verde")
    ];

    private readonly ActualizarMarcaOperador _actualizar;
    private readonly IOperadorRepositorio _operadores;

    public MarcaModel(ActualizarMarcaOperador actualizar, IOperadorRepositorio operadores)
    {
        _actualizar = actualizar;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public string? Mensaje { get; private set; }
    public string? Error { get; private set; }

    [BindProperty]
    public MarcaForm Formulario { get; set; } = new();

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        await CargarAsync();
        if (Operador is not null)
        {
            Formulario = new MarcaForm
            {
                ColorMarca = Colores.Any(color => color.Value == Operador.ColorMarca) ? Operador.ColorMarca : "violeta",
                TelefonoContacto = Operador.TelefonoContacto,
                EmailContacto = Operador.EmailContacto
            };
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        if (Colores.All(color => color.Value != Formulario.ColorMarca))
            ModelState.AddModelError("Formulario.ColorMarca", "Elegí uno de los colores disponibles.");

        if (!ModelState.IsValid)
        {
            await CargarAsync();
            return Page();
        }

        try
        {
            await _actualizar.EjecutarAsync(
                acceso.OperadorId,
                Formulario.ColorMarca,
                HttpContext.RequestAborted,
                Formulario.TelefonoContacto ?? string.Empty,
                Formulario.EmailContacto ?? string.Empty);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = TextosUsuario.Error(ex);
            await CargarAsync();
            return Page();
        }

        TempData["Mensaje"] = "Marca actualizada. El Portal, el seguimiento y los correos ya la usan.";
        return RedirectToPage();
    }

    public static string Css(string? color) => color switch
    {
        "azul" => "#0d6efd",
        "verde" => "#198754",
        _ => "#4f46e5"
    };

    private async Task CargarAsync()
    {
        if (AccesoActual.Intentar(User, out var acceso))
            Operador = await _operadores.ObtenerAsync(acceso.OperadorId, HttpContext.RequestAborted);
    }

    public sealed class MarcaForm
    {
        public string ColorMarca { get; set; } = "violeta";

        [StringLength(40, ErrorMessage = "El teléfono es demasiado largo.")]
        public string? TelefonoContacto { get; set; }

        [EmailAddress(ErrorMessage = "El email no es válido.")]
        [StringLength(160, ErrorMessage = "El email es demasiado largo.")]
        public string? EmailContacto { get; set; }
    }
}
