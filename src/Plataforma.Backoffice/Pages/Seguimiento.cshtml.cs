using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;

namespace Plataforma.Backoffice.Pages;

/// <summary>Vista pública mínima de seguimiento (sin datos personales).</summary>
[AllowAnonymous]
public class SeguimientoModel : PageModel
{
    private readonly ObtenerSeguimientoEnvio _seguimiento;

    public SeguimientoModel(ObtenerSeguimientoEnvio seguimiento)
    {
        _seguimiento = seguimiento;
    }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Numero { get; set; }

    public SeguimientoVisible? Visible { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Token))
            Visible = await _seguimiento.PorTokenAsync(Token, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(Numero))
            Visible = await _seguimiento.PorNumeroAsync(Numero, cancellationToken);

        if (Visible is null && (!string.IsNullOrWhiteSpace(Token) || !string.IsNullOrWhiteSpace(Numero)))
            Error = "No encontramos un envío con ese seguimiento.";
    }
}
