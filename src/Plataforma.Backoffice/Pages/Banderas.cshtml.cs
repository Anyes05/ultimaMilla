using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>Banderas de funcionalidad por operador (opcional 7.4): solo el administrador de plataforma.</summary>
[Authorize(Roles = PermisosPerfil.Plataforma)]
public class BanderasModel : PageModel
{
    private const int CambiosVisibles = 30;

    private readonly GestionBanderas _banderas;
    private readonly IOperadorRepositorio _operadores;

    public BanderasModel(GestionBanderas banderas, IOperadorRepositorio operadores)
    {
        _banderas = banderas;
        _operadores = operadores;
    }

    public IReadOnlyList<OperadorConBanderas> Operadores { get; private set; } = [];
    public IReadOnlyList<CambioBandera> Cambios { get; private set; } = [];
    public string? Mensaje { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;

        var ct = HttpContext.RequestAborted;
        var filas = new List<OperadorConBanderas>();
        foreach (var id in await _operadores.ListarIdsAsync(ct))
        {
            if (await _operadores.ObtenerAsync(id, ct) is { } operador)
                filas.Add(new OperadorConBanderas(operador, await _banderas.ListarPorOperadorAsync(id, ct)));
        }

        Operadores = filas.OrderBy(fila => fila.Operador.Nombre).ToList();
        Cambios = (await _banderas.HistorialAsync(null, ct)).Take(CambiosVisibles).ToList();
    }

    public async Task<IActionResult> OnPostCambiarAsync(Guid operadorId, string clave, bool activa)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        var ct = HttpContext.RequestAborted;
        try
        {
            var operador = await _operadores.ObtenerAsync(operadorId, ct)
                ?? throw new InvalidOperationException("El operador no existe.");
            var usuario = string.IsNullOrWhiteSpace(User.Identity?.Name) ? acceso.UsuarioId.ToString() : User.Identity.Name;
            await _banderas.CambiarAsync(operadorId, clave, activa, usuario, ct);
            TempData["Mensaje"] = $"{Descripcion(clave)}: {(activa ? "activada" : "desactivada")} para {operador.Nombre}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        // PRG: recargar no repite el cambio.
        return RedirectToPage();
    }

    public static string Descripcion(string clave) =>
        CatalogoBanderas.Todas.FirstOrDefault(bandera => bandera.Clave == clave)?.Descripcion ?? clave;

    public string NombreOperador(Guid operadorId) =>
        Operadores.FirstOrDefault(fila => fila.Operador.Id == operadorId)?.Operador.Nombre ?? "—";

    public sealed record OperadorConBanderas(Operador Operador, IReadOnlyList<BanderaVista> Banderas);
}
