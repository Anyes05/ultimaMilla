using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

public class IndexModel : PageModel
{
    private readonly IEnvioRepositorio _repositorio;

    public List<Envio> Envios { get; set; } = new();

    public IndexModel(IEnvioRepositorio repositorio)
    {
        _repositorio = repositorio;
    }

    public async Task OnGetAsync()
    {
        Envios = await _repositorio.ObtenerTodosAsync();
    }
}