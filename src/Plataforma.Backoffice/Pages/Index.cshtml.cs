using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

[Authorize(Roles = Roles.Administrador + "," + Roles.Despachador + "," + Roles.OperarioDeposito)]
public class IndexModel : PageModel
{
    private readonly IEnvioRepositorio _repositorio;
    private readonly ITransicionEnvio _transicion;
    private readonly IOperadorRepositorio _operadores;

    public List<Envio> Envios { get; set; } = new();
    public Operador? Operador { get; private set; }
    public string? Error { get; private set; }
    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    public IndexModel(IEnvioRepositorio repositorio, ITransicionEnvio transicion, IOperadorRepositorio operadores)
    {
        _repositorio = repositorio;
        _transicion = transicion;
        _operadores = operadores;
    }

    public async Task OnGetAsync()
    {
        Error = TempData["Error"] as string;
        await CargarAsync();
    }

    public async Task<IActionResult> OnPostCambiarEstadoAsync(Guid envioId, EstadoEnvio nuevoEstado)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            await _transicion.EjecutarAsync(envioId, acceso.OperadorId, nuevoEstado, User.Identity?.Name);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        // PRG: evita reenviar el mismo cambio al refrescar o con doble clic.
        return RedirectToPage();
    }

    public IReadOnlyList<EstadoEnvio> DestinosDesde(EstadoEnvio estado) =>
        _transicion.DestinosDesde(estado);

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        Envios = await _repositorio.ListarAsync(acceso.OperadorId, null);
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId);
    }
}