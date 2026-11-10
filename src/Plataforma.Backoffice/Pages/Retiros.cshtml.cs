using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>Retiros en domicilio pedidos por los comercios: asignarlos a una ruta y cerrarlos (RF 37 a 41).</summary>
[Authorize(Roles = PermisosPerfil.Planificacion)]
public class RetirosModel : PageModel
{
    private readonly GestionRetiros _retiros;
    private readonly GestionComercios _comercios;
    private readonly ListarRutasDelDia _rutas;
    private readonly IOperadorRepositorio _operadores;

    public RetirosModel(
        GestionRetiros retiros,
        GestionComercios comercios,
        ListarRutasDelDia rutas,
        IOperadorRepositorio operadores)
    {
        _retiros = retiros;
        _comercios = comercios;
        _rutas = rutas;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<SolicitudRetiro> Retiros { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> Comercios { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<DateOnly, IReadOnlyList<RutaDelDiaResumen>> RutasPorFecha { get; private set; } =
        new Dictionary<DateOnly, IReadOnlyList<RutaDelDiaResumen>>();
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;

        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Retiros = (await _retiros.ListarAsync(acceso.OperadorId, cancellationToken: ct))
            .OrderByDescending(retiro => retiro.FechaSolicitada)
            .ThenByDescending(retiro => retiro.CreadoEn)
            .ToList();
        Comercios = (await _comercios.ListarAsync(acceso.OperadorId, ct))
            .ToDictionary(comercio => comercio.Id, comercio => comercio.NombreComercial);

        var rutas = new Dictionary<DateOnly, IReadOnlyList<RutaDelDiaResumen>>();
        foreach (var fecha in Retiros.Where(EstaAbierto).Select(retiro => retiro.FechaSolicitada).Distinct())
            rutas[fecha] = await _rutas.EjecutarAsync(acceso.OperadorId, fecha, ct);
        RutasPorFecha = rutas;
    }

    public Task<IActionResult> OnPostProgramarAsync(Guid retiroId, Guid rutaId) => EjecutarAsync(async acceso =>
    {
        var ct = HttpContext.RequestAborted;
        var retiro = (await _retiros.ListarAsync(acceso.OperadorId, cancellationToken: ct)).FirstOrDefault(item => item.Id == retiroId)
            ?? throw new InvalidOperationException("El retiro no existe.");

        // La ruta tiene que ser de este operador y del día pedido.
        var rutas = await _rutas.EjecutarAsync(acceso.OperadorId, retiro.FechaSolicitada, ct);
        if (rutas.All(ruta => ruta.RutaId != rutaId))
            throw new InvalidOperationException("Elegí una ruta del día del retiro.");

        await _retiros.ProgramarAsync(acceso.OperadorId, retiroId, rutaId, ct);
        TempData["Mensaje"] = "Retiro asignado a la ruta.";
    });

    public Task<IActionResult> OnPostCompletarAsync(Guid retiroId) => EjecutarAsync(async acceso =>
    {
        await _retiros.CompletarAsync(acceso.OperadorId, retiroId, cancellationToken: HttpContext.RequestAborted);
        TempData["Mensaje"] = "Retiro completado.";
    });

    public Task<IActionResult> OnPostCancelarAsync(Guid retiroId) => EjecutarAsync(async acceso =>
    {
        await _retiros.CancelarAsync(acceso.OperadorId, retiroId, "Cancelado por el operador", HttpContext.RequestAborted);
        TempData["Mensaje"] = "Retiro cancelado.";
    });

    public static bool EstaAbierto(SolicitudRetiro retiro) =>
        retiro.Estado is EstadoRetiro.Solicitado or EstadoRetiro.Programado or EstadoRetiro.EnCurso;

    public static string NombreEstado(EstadoRetiro estado) => estado switch
    {
        EstadoRetiro.EnCurso => "En curso",
        _ => estado.ToString()
    };

    public static string ClaseEstado(EstadoRetiro estado) => estado switch
    {
        EstadoRetiro.Solicitado => "status-admitido",
        EstadoRetiro.Programado => "status-asignado-a-ruta",
        EstadoRetiro.EnCurso => "status-en-transito",
        EstadoRetiro.Completado => "status-entregado",
        _ => "status-extraviado"
    };

    private async Task<IActionResult> EjecutarAsync(Func<AccesoActual, Task> accion)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            await accion(acceso);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        // PRG: recargar no repite la acción.
        return RedirectToPage();
    }
}
