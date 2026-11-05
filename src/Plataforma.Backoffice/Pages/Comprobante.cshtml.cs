using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>Pruebas de entrega que envió la app del repartidor (RF 20), para resolver reclamos.</summary>
[Authorize(Roles = PermisosPerfil.Operacion)]
public class ComprobanteModel : PageModel
{
    private readonly ObtenerComprobantesEnvio _comprobantes;
    private readonly IOperadorRepositorio _operadores;

    public ComprobanteModel(ObtenerComprobantesEnvio comprobantes, IOperadorRepositorio operadores)
    {
        _comprobantes = comprobantes;
        _operadores = operadores;
    }

    [BindProperty(SupportsGet = true)]
    public Guid EnvioId { get; set; }

    public ComprobantesEnvio? Envio { get; private set; }
    public Operador? Operador { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        // El envío se busca dentro del operador: el de otro operador da 404, igual que uno inexistente.
        Envio = await _comprobantes.Ejecutar(acceso, EnvioId, HttpContext.RequestAborted);
        if (Envio is null)
            return NotFound();

        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, HttpContext.RequestAborted);
        return Page();
    }

    /// <summary>viewBox del SVG que encierra todos los trazos, con un margen para el grosor del trazo.</summary>
    public static string CajaFirma(IReadOnlyList<IReadOnlyList<PuntoFirma>> trazos)
    {
        var puntos = trazos.SelectMany(trazo => trazo).ToList();
        const double margen = 4;
        var minX = puntos.Min(p => p.X) - margen;
        var minY = puntos.Min(p => p.Y) - margen;
        var ancho = Math.Max(puntos.Max(p => p.X) - minX + margen, 1);
        var alto = Math.Max(puntos.Max(p => p.Y) - minY + margen, 1);
        return string.Join(' ', new[] { minX, minY, ancho, alto }.Select(Numero));
    }

    public static string PuntosTrazo(IReadOnlyList<PuntoFirma> trazo) =>
        string.Join(' ', trazo.Select(p => $"{Numero(p.X)},{Numero(p.Y)}"));

    public static string EnlaceMapa(decimal latitud, decimal longitud)
    {
        var lat = latitud.ToString(CultureInfo.InvariantCulture);
        var lon = longitud.ToString(CultureInfo.InvariantCulture);
        return $"https://www.openstreetmap.org/?mlat={lat}&mlon={lon}#map=18/{lat}/{lon}";
    }

    private static string Numero(double valor) => valor.ToString("0.#", CultureInfo.InvariantCulture);
}
