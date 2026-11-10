using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Avisos;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>
/// Mensajes fallidos (letra 6.8): el administrador ve los avisos a comercios que agotaron los reintentos
/// y los vuelve a encolar. Solo ve los de su operador.
/// </summary>
[Authorize(Roles = PermisosPerfil.Administracion)]
public class AvisosModel : PageModel
{
    private readonly ServicioAvisos _avisos;
    private readonly ListarEnvios _listarEnvios;
    private readonly IOperadorRepositorio _operadores;
    private readonly Plataforma.Infrastructure.PlataformaDbContext _contexto;
    private readonly ILogger<AvisosModel> _logger;
    private readonly IBanderasOperador _banderas;

    public AvisosModel(
        IBanderasOperador banderas,
        ServicioAvisos avisos,
        ListarEnvios listarEnvios,
        IOperadorRepositorio operadores,
        Plataforma.Infrastructure.PlataformaDbContext contexto,
        ILogger<AvisosModel> logger)
    {
        _avisos = avisos;
        _listarEnvios = listarEnvios;
        _operadores = operadores;
        _contexto = contexto;
        _logger = logger;
        _banderas = banderas;
    }

    /// <summary>Bandera "avisos-comercio" del operador: apagada, no se envían ni se reintentan.</summary>
    public bool AvisosActivos { get; private set; } = true;

    /// <summary>Caída simulada del receptor de prueba (configuración de Joel, por operador).</summary>
    public bool ReceptorFalla { get; private set; }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<EntregaVista> Entregas { get; private set; } = [];
    public IReadOnlyList<NotificacionFallidaVista> NotificacionesFallidas { get; private set; } = [];
    public ResumenAvisos Resumen { get; private set; } = new(0, 0, 0);
    public IReadOnlyDictionary<Guid, string> Tracking { get; private set; } = new Dictionary<Guid, string>();
    public string? Mensaje { get; private set; }
    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string Filtro { get; set; } = "fallidos";

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;

        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        ReceptorFalla = (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(
            _contexto.ReceptoresPruebaConfiguracion, item => item.OperadorId == acceso.OperadorId, ct))?.ForzarFallo == true;
        try
        {
            var estado = Filtro == "todos" ? (EstadoEntrega?)null : EstadoEntrega.Fallido;
            AvisosActivos = await _banderas.EstaActivaAsync(acceso.OperadorId, CatalogoBanderas.AvisosComercio, ct);
            Entregas = await _avisos.ListarEntregasOperadorAsync(acceso.OperadorId, estado, 200, ct);
            Resumen = await _avisos.ResumenOperadorAsync(acceso.OperadorId, ct);
            NotificacionesFallidas = await _avisos.ListarNotificacionesFallidasAsync(acceso.OperadorId, cancellationToken: ct);
            Tracking = (await _listarEnvios.Ejecutar(acceso, ct))
                .ToDictionary(envio => envio.Id, envio => envio.NumeroSeguimiento);
        }
        catch (Npgsql.PostgresException ex)
        {
            _logger.LogError(ex, "No se pudo leer el módulo de avisos.");
            Error ??= "Los avisos no están disponibles en este momento. Intentá de nuevo en unos minutos.";
        }
    }

    public async Task<IActionResult> OnPostCambiarReceptorAsync(bool forzarFallo)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        await AvisosTecnicosApi.CambiarReceptorAsync(_contexto, acceso.OperadorId, forzarFallo, HttpContext.RequestAborted);
        TempData["Mensaje"] = forzarFallo
            ? "Receptor de prueba en falla: los avisos nuevos van a fallar y se reintentarán automáticamente."
            : "Receptor de prueba disponible.";
        return RedirectToPage(new { Filtro });
    }

    public async Task<IActionResult> OnPostReintentarNotificacionAsync(Guid notificacionId)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            await _avisos.ReintentarNotificacionAsync(notificacionId, acceso.OperadorId, HttpContext.RequestAborted);
            TempData["Mensaje"] = "Notificación reenviada al destinatario.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        return RedirectToPage(new { Filtro });
    }

    public async Task<IActionResult> OnPostReintentarAsync(Guid entregaId)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        if (!await _banderas.EstaActivaAsync(acceso.OperadorId, CatalogoBanderas.AvisosComercio, HttpContext.RequestAborted))
        {
            TempData["Error"] = "Los avisos a comercios están apagados para este operador: no se reintentan.";
            return RedirectToPage(new { Filtro });
        }

        try
        {
            await _avisos.ReintentarAsync(entregaId, acceso.OperadorId, cancellationToken: HttpContext.RequestAborted);
            TempData["Mensaje"] = "Aviso reenviado: se vuelve a intentar con el mismo identificador de evento, así el comercio no lo recibe duplicado.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Quedó Pendiente en la base: el barrido del worker lo encola aunque la cola no haya respondido ahora.
            _logger.LogWarning(ex, "Reintento {EntregaId} guardado pero no publicado.", entregaId);
            TempData["Mensaje"] = "Reintento registrado: el aviso se enviará en unos segundos.";
        }

        return RedirectToPage(new { Filtro });
    }
}
