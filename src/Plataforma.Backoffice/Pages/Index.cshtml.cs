using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

[Authorize(Roles = PermisosPerfil.Operacion)]
public class IndexModel : PageModel
{
    private readonly ListarEnvios _listarEnvios;
    private readonly ITransicionEnvio _transicion;
    private readonly IOperadorRepositorio _operadores;
    private readonly IEnvioRepositorio _envios;

    public List<Envio> Envios { get; set; } = new();
    public int TotalEnvios { get; private set; }
    public int Pagina { get; private set; } = 1;
    public int TamanoPagina { get; private set; } = 50;
    public EstadoEnvio? FiltroEstado { get; private set; }
    public Operador? Operador { get; private set; }
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }
    public string? Referencia { get; private set; }
    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    public IndexModel(ListarEnvios listarEnvios, ITransicionEnvio transicion, IOperadorRepositorio operadores, IEnvioRepositorio envios)
    {
        _listarEnvios = listarEnvios;
        _transicion = transicion;
        _operadores = operadores;
        _envios = envios;
    }

    /// <summary>Rol del usuario autenticado (vacío si no hay sesión válida).</summary>
    private string Rol => AccesoActual.Intentar(User, out var acceso) ? acceso.Rol : string.Empty;

    public async Task OnGetAsync(EstadoEnvio? estado = null, int pagina = 1, bool sinPermiso = false)
    {
        Error = sinPermiso ? "Tu perfil no tiene acceso a esa página." : TempData["Error"] as string;
        Mensaje = TempData["Mensaje"] as string;
        Referencia = TempData["Referencia"] as string;
        FiltroEstado = estado;
        Pagina = pagina < 1 ? 1 : pagina;
        await CargarAsync();
    }

    public async Task<IActionResult> OnPostCambiarEstadoAsync(Guid envioId, EstadoEnvio nuevoEstado)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        // Autorización en el servidor: no alcanza con esconder el botón (letra 6.3).
        var envio = await _envios.ObtenerPorOperadorAsync(envioId, acceso.OperadorId, HttpContext.RequestAborted);
        if (envio is null)
        {
            TempData["Error"] = "El envío no existe.";
            return RedirectToPage();
        }

        if (!PermisosPerfil.PuedeCambiar(acceso.Rol, envio.Estado, nuevoEstado))
        {
            TempData["Error"] = "Tu perfil no puede hacer ese cambio de estado.";
            return RedirectToPage();
        }

        try
        {
            var resultado = await _transicion.EjecutarAsync(
                envioId, acceso.OperadorId, nuevoEstado, User.Identity?.Name);

            // Mensaje para el usuario con nombres legibles; el EventId queda como referencia corta
            // (sirve para seguir el evento en los logs del worker sin ensuciar el mensaje).
            TempData["Mensaje"] =
                $"{envio.NumeroSeguimiento} pasó de {EstadosVista.Nombre(resultado.EstadoAnterior)} a {EstadosVista.Nombre(resultado.EstadoNuevo)}.";
            if (resultado.EventId is Guid eventId)
                TempData["Referencia"] = eventId.ToString()[..8];
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        // PRG: evita reenviar el mismo cambio al refrescar o con doble clic.
        return RedirectToPage();
    }

    /// <summary>Solo los cambios válidos que además puede hacer el perfil del usuario.</summary>
    public IReadOnlyList<EstadoEnvio> DestinosDesde(EstadoEnvio estado) =>
        PermisosPerfil.DestinosPermitidos(Rol, estado, _transicion.DestinosDesde(estado));

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var pagina = await _listarEnvios.EjecutarPaginadoAsync(
            acceso,
            FiltroEstado,
            Pagina,
            TamanoPagina,
            HttpContext.RequestAborted);
        Envios = pagina.Items.ToList();
        TotalEnvios = pagina.Total;
        Pagina = pagina.Pagina;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId);
    }
}