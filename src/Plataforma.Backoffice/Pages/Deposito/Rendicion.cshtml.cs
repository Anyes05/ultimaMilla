using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages.Deposito;

/// <summary>
/// Rendición de devoluciones en depósito (RF 22, transición 13 de la tabla 4.2: EnDevolucion → Devuelto).
/// El operario ingresa o escanea el número de seguimiento del bulto que vuelve; un lector de códigos
/// "teclea" el número y envía Enter, así que el mismo formulario sirve con lector o a mano.
/// El cambio pasa por el caso de uso de transición: historial, outbox, aviso al comercio y seguimiento.
/// </summary>
[Authorize(Roles = PermisosPerfil.Deposito)]
public class RendicionModel : PageModel
{
    private const string Motivo = "Rendición en depósito";

    private readonly ListarEnvios _listarEnvios;
    private readonly ITransicionEnvio _transicion;
    private readonly IEnvioRepositorio _envios;

    public RendicionModel(ListarEnvios listarEnvios, ITransicionEnvio transicion, IEnvioRepositorio envios)
    {
        _listarEnvios = listarEnvios;
        _transicion = transicion;
        _envios = envios;
    }

    public IReadOnlyList<Envio> Pendientes { get; private set; } = [];
    public int TotalPendientes { get; private set; }
    public int Pagina { get; private set; } = 1;
    public int TamanoPagina { get; } = 50;
    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(TotalPendientes / (double)TamanoPagina));
    public string? Mensaje { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(int pagina = 1)
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;
        Pagina = pagina < 1 ? 1 : pagina;
        await CargarAsync();
    }

    /// <summary>Rinde por número de seguimiento (lector de códigos o teclado).</summary>
    public async Task<IActionResult> OnPostEscanearAsync(string? numeroSeguimiento)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        var numero = numeroSeguimiento?.Trim() ?? string.Empty;
        if (numero.Length == 0)
        {
            TempData["Error"] = "Ingresá o escaneá un número de seguimiento.";
            return RedirectToPage();
        }

        // El número es único dentro del operador; se confirma el operador además del filtro global.
        var encontrado = await _envios.ObtenerPorNumeroAsync(numero, HttpContext.RequestAborted);
        if (encontrado is null || encontrado.OperadorId != acceso.OperadorId)
        {
            TempData["Error"] = $"No hay ningún envío con el número {numero}.";
            return RedirectToPage();
        }

        return await RendirAsync(acceso, encontrado.Id);
    }

    /// <summary>Rinde desde la lista de pendientes.</summary>
    public async Task<IActionResult> OnPostRendirAsync(Guid envioId)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        return await RendirAsync(acceso, envioId);
    }

    private async Task<IActionResult> RendirAsync(AccesoActual acceso, Guid envioId)
    {
        var envio = await _envios.ObtenerPorOperadorAsync(envioId, acceso.OperadorId, HttpContext.RequestAborted);
        if (envio is null)
        {
            TempData["Error"] = "El envío no existe.";
            return RedirectToPage();
        }

        // Mensaje claro para el error más común en el mostrador: escanear un bulto que no está en devolución.
        if (envio.Estado != EstadoEnvio.EnDevolucion)
        {
            TempData["Error"] = envio.Estado == EstadoEnvio.Devuelto
                ? $"{envio.NumeroSeguimiento} ya fue rendido."
                : $"{envio.NumeroSeguimiento} está {EstadosVista.Nombre(envio.Estado).ToLowerInvariant()}: solo se rinden envíos en devolución.";
            return RedirectToPage();
        }

        // Autorización en el servidor (letra 6.3): la página ya exige el perfil, y la transición también.
        if (!PermisosPerfil.PuedeCambiar(acceso.Rol, envio.Estado, EstadoEnvio.Devuelto))
        {
            TempData["Error"] = "Tu perfil no puede rendir devoluciones.";
            return RedirectToPage();
        }

        try
        {
            await _transicion.EjecutarAsync(
                envio.Id, acceso.OperadorId, EstadoEnvio.Devuelto, User.Identity?.Name, Motivo, HttpContext.RequestAborted);
            TempData["Mensaje"] = $"{envio.NumeroSeguimiento} rendido: quedó como devuelto.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Por ejemplo, otro operario lo rindió un instante antes.
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        // PRG: refrescar la página no vuelve a rendir.
        return RedirectToPage();
    }

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var resultado = await _listarEnvios.EjecutarPaginadoAsync(
            acceso, EstadoEnvio.EnDevolucion, Pagina, TamanoPagina, HttpContext.RequestAborted);
        Pendientes = resultado.Items;
        TotalPendientes = resultado.Total;
        Pagina = resultado.Pagina;
    }
}
