using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>
/// Arma y despacha una ruta: elige repartidor, fecha y envíos en depósito.
/// Usa los casos de uso de Application directamente (sin HTTP interno).
/// </summary>
[Authorize(Roles = PermisosPerfil.Planificacion)]
public class DespachoModel : PageModel
{
    private readonly ListarEnvios _listarEnvios;
    private readonly ListarRepartidoresActivos _listarRepartidores;
    private readonly ListarRutasDelDia _listarRutas;
    private readonly DespacharRuta _despachar;
    private readonly IniciarRuta _iniciarRuta;
    private readonly IOperadorRepositorio _operadores;
    private readonly GestionCobertura _cobertura;
    private readonly GestionRetiros _retiros;
    private readonly ReasignarEnvio _reasignar;

    /// <summary>RF 39: carga del vehículo parada por parada, con los retiros de la ruta.</summary>
    public IReadOnlyDictionary<Guid, PerfilCargaRuta> Perfiles { get; private set; } = new Dictionary<Guid, PerfilCargaRuta>();

    public DespachoModel(
        ReasignarEnvio reasignar,
        GestionRetiros retiros,
        GestionCobertura cobertura,
        ListarEnvios listarEnvios,
        ListarRepartidoresActivos listarRepartidores,
        ListarRutasDelDia listarRutas,
        DespacharRuta despachar,
        IniciarRuta iniciarRuta,
        IOperadorRepositorio operadores)
    {
        _listarEnvios = listarEnvios;
        _listarRepartidores = listarRepartidores;
        _listarRutas = listarRutas;
        _despachar = despachar;
        _iniciarRuta = iniciarRuta;
        _operadores = operadores;
        _cobertura = cobertura;
        _retiros = retiros;
        _reasignar = reasignar;
    }

    /// <summary>
    /// Pasa un envío de la ruta a otro repartidor en la misma jornada (si no tiene ruta ese día, se le crea).
    /// Urgente publica <c>EnvioReasignado</c> con prioridad: el worker manda el push al repartidor nuevo (7.3).
    /// La app del repartidor anterior ve el cambio al bajar la ruta, y si lo entregó sin señal, el servidor
    /// rechaza ese comprobante con motivo (ADR 0013).
    /// </summary>
    public async Task<IActionResult> OnPostReasignarAsync(Guid envioId, Guid repartidorNuevoId, DateOnly fechaRuta, bool urgente)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            if (repartidorNuevoId == Guid.Empty)
                throw new ArgumentException("Elegí a qué repartidor reasignar.");
            var ct = HttpContext.RequestAborted;
            var fecha = fechaRuta == default
                ? (await _operadores.ObtenerAsync(acceso.OperadorId, ct))?.DiaActual() ?? DateOnly.FromDateTime(DateTime.UtcNow)
                : fechaRuta;
            await _reasignar.EjecutarAsync(acceso.OperadorId, envioId, repartidorNuevoId, fecha, urgente, ct);
            var nombre = (await _listarRepartidores.EjecutarAsync(acceso.OperadorId, ct))
                .FirstOrDefault(repartidor => repartidor.Id == repartidorNuevoId)?.Nombre ?? "el repartidor elegido";
            TempData["Mensaje"] = urgente
                ? $"Envío reasignado a {nombre}. Se le avisa por notificación push."
                : $"Envío reasignado a {nombre}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        return RedirectToPage();
    }

    /// <summary>Vehículos activos: elegir uno valida paradas, peso y volumen (RF 13 y 14).</summary>
    public IReadOnlyList<Vehiculo> Vehiculos { get; private set; } = [];

    public IReadOnlyDictionary<Guid, FranjaHoraria> Franjas { get; private set; } = new Dictionary<Guid, FranjaHoraria>();

    [BindProperty]
    public Guid? VehiculoId { get; set; }

    public string NombreFranja(Guid? franjaId) =>
        franjaId is Guid id && Franjas.TryGetValue(id, out var franja)
            ? $"{franja.Nombre} ({franja.Desde:HH\\:mm} a {franja.Hasta:HH\\:mm})"
            : "Sin franja";

    public Operador? Operador { get; private set; }
    public IReadOnlyList<Envio> EnviosEnDeposito { get; private set; } = [];
    public IReadOnlyList<RepartidorVisible> Repartidores { get; private set; } = [];
    public IReadOnlyList<RutaDelDiaResumen> RutasDeHoy { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    [BindProperty]
    public Guid RepartidorId { get; set; }

    [BindProperty]
    public DateOnly Fecha { get; set; }

    [BindProperty]
    public List<Guid> EnvioIds { get; set; } = [];

    // Despachar e iniciar en un solo paso; desmarcado, la ruta queda esperando "Iniciar ruta".
    [BindProperty]
    public bool IniciarAlDespachar { get; set; } = true;

    public async Task OnGetAsync()
    {
        Error = TempData["Error"] as string;
        Mensaje = TempData["Mensaje"] as string;
        await CargarAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        if (RepartidorId == Guid.Empty)
        {
            TempData["Error"] = "Elegí un repartidor.";
            return RedirectToPage();
        }

        if (EnvioIds.Count == 0)
        {
            TempData["Error"] = "Seleccioná al menos un envío para la ruta.";
            return RedirectToPage();
        }

        try
        {
            var ruta = await _despachar.EjecutarAsync(
                acceso.OperadorId,
                RepartidorId,
                Fecha,
                EnvioIds.Distinct().ToList(),
                vehiculoId: VehiculoId,
                cancellationToken: HttpContext.RequestAborted);

            TempData["Mensaje"] =
                $"Ruta despachada para el {ruta.Fecha:dd/MM/yyyy} con {EnvioIds.Distinct().Count()} envío(s).";

            if (IniciarAlDespachar)
                await EjecutarInicioAsync(ruta.Id, acceso.OperadorId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        // PRG: recargar no vuelve a despachar.
        return RedirectToPage();
    }

    /// <summary>
    /// Sale la ruta: pasa sus envíos AsignadoARuta → EnTransito.
    /// La app del repartidor solo puede entregar envíos en tránsito.
    /// </summary>
    public async Task<IActionResult> OnPostIniciarRutaAsync(Guid rutaId)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            await EjecutarInicioAsync(rutaId, acceso.OperadorId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        // PRG: recargar no vuelve a iniciar.
        return RedirectToPage();
    }

    private async Task EjecutarInicioAsync(Guid rutaId, Guid operadorId)
    {
        var resultado = await _iniciarRuta.EjecutarAsync(
            operadorId,
            rutaId,
            User.Identity?.Name,
            HttpContext.RequestAborted);

        var retirosEnCurso = await _retiros.IniciarRutaAsync(operadorId, rutaId, HttpContext.RequestAborted);
        TempData["Mensaje"] =
            $"{TempData.Peek("Mensaje")} Ruta iniciada: {resultado.Iniciados} envío(s) en tránsito{(retirosEnCurso > 0 ? $" y {retirosEnCurso} retiro(s) en curso" : "")}.".Trim();
        if (resultado.Errores.Count > 0)
            TempData["Error"] = string.Join(" ", resultado.Errores);
    }

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId);
        if (Fecha == default)
            Fecha = Operador?.DiaActual() ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var envios = await _listarEnvios.Ejecutar(acceso, ct);
        // Despachables: en depósito o reprogramados, sin RutaId (DespacharRuta asigna AsignadoARuta).
        EnviosEnDeposito = envios
            .Where(envio =>
                envio.RutaId is null &&
                envio.Estado is EstadoEnvio.EnDeposito or EstadoEnvio.Reprogramado
                    or EstadoEnvio.EntregadoParcialmente)
            .OrderBy(envio => envio.FechaCreacion)
            .ToList();
        Repartidores = await _listarRepartidores.EjecutarAsync(acceso.OperadorId, ct);
        Vehiculos = (await _cobertura.ListarVehiculosAsync(acceso.OperadorId, ct)).OrderBy(vehiculo => vehiculo.Patente).ToList();
        Franjas = (await _cobertura.ListarTodasLasFranjasAsync(acceso.OperadorId, ct)).ToDictionary(franja => franja.Id);
        RutasDeHoy = await _listarRutas.EjecutarAsync(acceso.OperadorId, Fecha, ct);
        var perfiles = new Dictionary<Guid, PerfilCargaRuta>();
        foreach (var ruta in RutasDeHoy)
        {
            if (await _retiros.PerfilCargaAsync(acceso.OperadorId, ruta.RutaId, ct) is { } perfil)
                perfiles[ruta.RutaId] = perfil;
        }
        Perfiles = perfiles;
    }
}
