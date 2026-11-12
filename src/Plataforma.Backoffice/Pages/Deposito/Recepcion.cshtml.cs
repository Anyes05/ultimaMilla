using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages.Deposito;

/// <summary>Recepción en depósito por escaneo de bultos, con registro de los que faltan (RF 10).</summary>
[Authorize(Roles = PermisosPerfil.Deposito)]
public class RecepcionModel : PageModel
{
    private const int TamanoPagina = 25;
    private const int DiscrepanciasVisibles = 20;

    private readonly ListarEnvios _listarEnvios;
    private readonly RecibirEnvioEnDeposito _recibir;
    private readonly IEnvioRepositorio _envios;
    private readonly IBultoRepositorio _bultos;
    private readonly IRecepcionRepositorio _recepciones;

    public RecepcionModel(
        ListarEnvios listarEnvios,
        RecibirEnvioEnDeposito recibir,
        IEnvioRepositorio envios,
        IBultoRepositorio bultos,
        IRecepcionRepositorio recepciones)
    {
        _listarEnvios = listarEnvios;
        _recibir = recibir;
        _envios = envios;
        _bultos = bultos;
        _recepciones = recepciones;
    }

    public IReadOnlyList<PendienteRecepcion> Pendientes { get; private set; } = [];
    public IReadOnlyList<DiscrepanciaRecepcion> Discrepancias { get; private set; } = [];
    public int TotalPendientes { get; private set; }
    public int Pagina { get; private set; } = 1;
    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(TotalPendientes / (double)TamanoPagina));
    public string? Mensaje { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(int pagina = 1)
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;
        Pagina = pagina < 1 ? 1 : pagina;

        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        var resultado = await _listarEnvios.EjecutarPaginadoAsync(acceso, EstadoEnvio.Admitido, Pagina, TamanoPagina, ct);
        TotalPendientes = resultado.Total;
        Pagina = resultado.Pagina;

        var pendientes = new List<PendienteRecepcion>(resultado.Items.Count);
        foreach (var envio in resultado.Items)
            pendientes.Add(new PendienteRecepcion(envio, await EsperadosAsync(acceso.OperadorId, envio.Id, ct)));
        Pendientes = pendientes;

        Discrepancias = await _recepciones.ListarDiscrepanciasAsync(acceso.OperadorId, DiscrepanciasVisibles, ct);
    }

    /// <summary>Recibe desde la lista (envioId) o por lector de códigos (numeroSeguimiento).</summary>
    public async Task<IActionResult> OnPostRecibirAsync(Guid? envioId, string? numeroSeguimiento, int? bultosRecibidos, string? observaciones)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        var ct = HttpContext.RequestAborted;
        var id = envioId;
        if (id is null)
        {
            var numero = numeroSeguimiento?.Trim() ?? string.Empty;
            var encontrado = numero.Length == 0 ? null : await _envios.ObtenerPorNumeroAsync(numero, ct);
            if (encontrado is null || encontrado.OperadorId != acceso.OperadorId)
            {
                TempData["Error"] = numero.Length == 0
                    ? "Ingresá o escaneá un número de seguimiento."
                    : $"No hay ningún envío con el número {numero}.";
                return RedirectToPage();
            }

            id = encontrado.Id;
        }

        if (bultosRecibidos < 0)
        {
            TempData["Error"] = "La cantidad de bultos recibidos no puede ser negativa.";
            return RedirectToPage();
        }

        try
        {
            // Sin cantidad se entiende que llegó completo, que es el caso del lector de códigos.
            var recibidos = bultosRecibidos ?? await EsperadosAsync(acceso.OperadorId, id.Value, ct);
            var usuario = string.IsNullOrWhiteSpace(User.Identity?.Name) ? acceso.UsuarioId.ToString() : User.Identity.Name;
            var resultado = await _recibir.EjecutarAsync(acceso.OperadorId, id.Value, recibidos, usuario, observaciones ?? string.Empty, ct);
            TempData["Mensaje"] = resultado.HayDiscrepancia
                ? $"Recibido con discrepancia: esperados {resultado.BultosEsperados}, recibidos {resultado.BultosRecibidos}."
                : "Envío recibido en depósito.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        // PRG: recargar no vuelve a recibir.
        return RedirectToPage();
    }

    /// <summary>RF 10: recibe lo escaneado (códigos de bultos o números de seguimiento), uno por línea.</summary>
    public async Task<IActionResult> OnPostEscanearAsync(string? codigos, string? observaciones)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        try
        {
            var usuario = string.IsNullOrWhiteSpace(User.Identity?.Name) ? acceso.UsuarioId.ToString() : User.Identity.Name;
            var lista = (codigos ?? string.Empty).Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var resultado = await _recibir.RecibirPorEscaneoAsync(
                acceso.OperadorId, lista, usuario, observaciones ?? string.Empty, HttpContext.RequestAborted);

            if (resultado.Recibidos.Count > 0)
                TempData["Mensaje"] = string.Join(" ", resultado.Recibidos.Select(recibido => recibido.Faltantes.Count == 0
                    ? $"{recibido.NumeroSeguimiento}: completo ({recibido.BultosRecibidos} de {recibido.BultosEsperados})."
                    : $"{recibido.NumeroSeguimiento}: con discrepancia, llegaron {recibido.BultosRecibidos} de {recibido.BultosEsperados}; faltan {string.Join(", ", recibido.Faltantes)}."));
            var problemas = resultado.NoReconocidos.Select(codigo => $"No es de ningún envío pendiente: {codigo}.")
                .Concat(resultado.Errores)
                .ToList();
            if (problemas.Count > 0)
                TempData["Error"] = string.Join(" ", problemas);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        // PRG: recargar no vuelve a recibir.
        return RedirectToPage();
    }

    private async Task<int> EsperadosAsync(Guid operadorId, Guid envioId, CancellationToken ct) =>
        Math.Max(1, (await _bultos.ListarPorEnvioAsync(operadorId, envioId, ct)).Count);

    public sealed record PendienteRecepcion(Envio Envio, int BultosEsperados);
}
