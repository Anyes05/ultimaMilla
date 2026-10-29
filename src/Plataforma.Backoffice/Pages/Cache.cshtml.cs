using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>Vista técnica del caché: aciertos, fallos y relación de aciertos.</summary>
[Authorize(Roles = PermisosPerfil.Administracion)]
public class CacheModel : PageModel
{
    private readonly ICacheLectura _cache;
    private readonly IOperadorRepositorio _operadores;

    public CacheModel(ICacheLectura cache, IOperadorRepositorio operadores)
    {
        _cache = cache;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public EstadisticasCache Estadisticas { get; private set; } = new(0, 0);
    public DateTime ConsultadoEn { get; private set; }

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    public string RatioTexto => Estadisticas.Ratio.ToString("P1", System.Globalization.CultureInfo.GetCultureInfo("es-UY"));

    public async Task OnGetAsync()
    {
        Estadisticas = _cache.Estadisticas();
        ConsultadoEn = DateTime.Now;

        if (AccesoActual.Intentar(User, out var acceso))
            Operador = await _operadores.ObtenerAsync(acceso.OperadorId, HttpContext.RequestAborted);
    }
}
