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
    private readonly IBanderasOperador _banderas;

    public RetirosModel(
        IBanderasOperador banderas,
        GestionRetiros retiros,
        GestionComercios comercios,
        ListarRutasDelDia rutas,
        IOperadorRepositorio operadores)
    {
        _retiros = retiros;
        _comercios = comercios;
        _rutas = rutas;
        _operadores = operadores;
        _banderas = banderas;
    }

    /// <summary>Bandera "retiro-domicilio": apagada, los comercios no pueden pedir; lo ya pedido se sigue gestionando.</summary>
    public bool RetirosActivos { get; private set; } = true;

    public Operador? Operador { get; private set; }
    public IReadOnlyList<SolicitudRetiro> Retiros { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> Comercios { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<DateOnly, IReadOnlyList<RutaDelDiaResumen>> RutasPorFecha { get; private set; } =
        new Dictionary<DateOnly, IReadOnlyList<RutaDelDiaResumen>>();
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }
    public DateOnly Hoy { get; private set; }
    public IReadOnlyDictionary<Guid, PerfilCargaRuta> Perfiles { get; private set; } = new Dictionary<Guid, PerfilCargaRuta>();

    /// <summary>Un no retirado se puede volver a visitar hoy o el día pedido, si todavía no pasó.</summary>
    public DateOnly FechaParaRutas(SolicitudRetiro retiro) =>
        retiro.Estado == EstadoRetiro.NoRetirado && Hoy > retiro.FechaSolicitada ? Hoy : retiro.FechaSolicitada;

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
        RetirosActivos = await _banderas.EstaActivaAsync(acceso.OperadorId, CatalogoBanderas.RetiroDomicilio, ct);
        Retiros = (await _retiros.ListarAsync(acceso.OperadorId, cancellationToken: ct))
            .OrderByDescending(retiro => retiro.FechaSolicitada)
            .ThenByDescending(retiro => retiro.CreadoEn)
            .ToList();
        Comercios = (await _comercios.ListarAsync(acceso.OperadorId, ct))
            .ToDictionary(comercio => comercio.Id, comercio => comercio.NombreComercial);

        Hoy = Operador?.DiaActual() ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var rutas = new Dictionary<DateOnly, IReadOnlyList<RutaDelDiaResumen>>();
        foreach (var fecha in Retiros.Where(EstaAbierto).Select(FechaParaRutas).Distinct())
            rutas[fecha] = await _rutas.EjecutarAsync(acceso.OperadorId, fecha, ct);
        RutasPorFecha = rutas;

        // RF 39: carga del vehículo a lo largo de cada ruta que ya tiene retiros.
        var perfiles = new Dictionary<Guid, PerfilCargaRuta>();
        foreach (var rutaId in Retiros.Where(retiro => retiro.RutaId is not null && retiro.Estado is EstadoRetiro.Programado or EstadoRetiro.EnCurso)
                     .Select(retiro => retiro.RutaId!.Value).Distinct())
        {
            if (await _retiros.PerfilCargaAsync(acceso.OperadorId, rutaId, ct) is { } perfil)
                perfiles[rutaId] = perfil;
        }
        Perfiles = perfiles;
    }

    public Task<IActionResult> OnPostProgramarAsync(Guid retiroId, Guid rutaId, int? posicion) => EjecutarAsync(async acceso =>
    {
        var ct = HttpContext.RequestAborted;
        var retiro = (await _retiros.ListarAsync(acceso.OperadorId, cancellationToken: ct)).FirstOrDefault(item => item.Id == retiroId)
            ?? throw new InvalidOperationException("El retiro no existe.");

        // La ruta tiene que ser de este operador y del día del retiro (o de hoy, si es una nueva visita).
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Hoy = Operador?.DiaActual() ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var rutas = await _rutas.EjecutarAsync(acceso.OperadorId, FechaParaRutas(retiro), ct);
        if (rutas.All(ruta => ruta.RutaId != rutaId))
            throw new InvalidOperationException("Elegí una ruta del día del retiro.");

        await _retiros.ProgramarAsync(acceso.OperadorId, retiroId, rutaId, ct, posicion);
        TempData["Mensaje"] = "Retiro asignado a la ruta.";
    });

    public Task<IActionResult> OnPostNoRetiradoAsync(Guid retiroId, string? motivo) => EjecutarAsync(async acceso =>
    {
        await _retiros.NoRetirarAsync(acceso.OperadorId, retiroId, motivo ?? string.Empty, HttpContext.RequestAborted);
        TempData["Mensaje"] = "Retiro marcado como no retirado: se puede asignar a otra ruta.";
    });

    public Task<IActionResult> OnPostRecibirAsync(Guid retiroId) => EjecutarAsync(async acceso =>
    {
        await _retiros.RecibirEnDepositoAsync(acceso.OperadorId, retiroId, HttpContext.RequestAborted);
        TempData["Mensaje"] = "Retiro recibido en el depósito.";
    });

    public Task<IActionResult> OnPostCompletarAsync(Guid retiroId) => EjecutarAsync(async acceso =>
    {
        await _retiros.CompletarAsync(acceso.OperadorId, retiroId, cancellationToken: HttpContext.RequestAborted);
        TempData["Mensaje"] = "Retiro marcado como retirado.";
    });

    public Task<IActionResult> OnPostCancelarAsync(Guid retiroId) => EjecutarAsync(async acceso =>
    {
        await _retiros.CancelarAsync(acceso.OperadorId, retiroId, "Cancelado por el operador", HttpContext.RequestAborted);
        TempData["Mensaje"] = "Retiro cancelado.";
    });

    /// <summary>Todavía hay que ir a retirarlo (incluye los no retirados, que esperan otra visita).</summary>
    public static bool EstaAbierto(SolicitudRetiro retiro) =>
        retiro.Estado is EstadoRetiro.Solicitado or EstadoRetiro.Programado or EstadoRetiro.EnCurso or EstadoRetiro.NoRetirado;

    public static string NombreEstado(EstadoRetiro estado) => estado switch
    {
        EstadoRetiro.EnCurso => "En curso",
        EstadoRetiro.Completado => "Retirado",
        EstadoRetiro.NoRetirado => "No retirado",
        EstadoRetiro.EnDeposito => "En depósito",
        _ => estado.ToString()
    };

    public static string ClaseEstado(EstadoRetiro estado) => estado switch
    {
        EstadoRetiro.Solicitado => "status-admitido",
        EstadoRetiro.Programado => "status-asignado-a-ruta",
        EstadoRetiro.EnCurso => "status-en-transito",
        EstadoRetiro.Completado => "status-entregado",
        EstadoRetiro.EnDeposito => "status-en-deposito",
        EstadoRetiro.NoRetirado => "status-no-entregado",
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
