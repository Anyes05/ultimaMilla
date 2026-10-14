using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Plataforma.Backoffice.Pages;

public class LoginModel : PageModel
{
    [BindProperty]
    public string Usuario { get; set; } = string.Empty;

    [BindProperty]
    public string Clave { get; set; } = string.Empty;

    [FromQuery(Name = "error")]
    public string? ErrorQuery { get; set; }

    public bool Error => ErrorQuery == "1";
}
