using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Application.Mensajes;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Backoffice.Pages;

[Authorize(Roles = Roles.Administrador + "," + Roles.Despachador + "," + Roles.OperarioDeposito)]
public class AvisosModel : PageModel
{
    private readonly PlataformaDbContext _contexto;
    private readonly IPublicadorMensajes _publicador;

    public AvisosModel(PlataformaDbContext contexto, IPublicadorMensajes publicador)
    {
        _contexto = contexto;
        _publicador = publicador;
    }

    public List<AvisoFallido> Fallidos { get; private set; } = [];
    public bool ReceptorFalla { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Fallidos = await _contexto.AvisosFallidos
            .Where(aviso => aviso.ResueltoEn == null)
            .OrderByDescending(aviso => aviso.ActualizadoEn)
            .ToListAsync(cancellationToken);
        if (AccesoActual.Intentar(User, out var acceso))
            ReceptorFalla = (await _contexto.ReceptoresPruebaConfiguracion
                .SingleOrDefaultAsync(item => item.OperadorId == acceso.OperadorId, cancellationToken))?.ForzarFallo == true;
    }

    public async Task<IActionResult> OnPostReintentarAsync(Guid id, CancellationToken cancellationToken)
    {
        var fallido = await _contexto.AvisosFallidos.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (fallido is null) return NotFound();
        var evento = EnvioEstadoCambiado.Deserializar(fallido.Payload);
        if (evento is null) return BadRequest();
        await _publicador.PublicarAsync(EnvioEstadoCambiado.Tipo, EnvioEstadoCambiado.Version, evento.EventId, fallido.Payload, cancellationToken);
        TempData["Mensaje"] = $"Se republicó el evento {evento.EventId}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCambiarReceptorAsync(bool forzarFallo, CancellationToken cancellationToken)
    {
        if (!AccesoActual.Intentar(User, out var acceso)) return Challenge();
        var receptor = await _contexto.ReceptoresPruebaConfiguracion.SingleOrDefaultAsync(item => item.OperadorId == acceso.OperadorId, cancellationToken);
        if (receptor is null)
            _contexto.ReceptoresPruebaConfiguracion.Add(ReceptorPruebaConfiguracion.Crear(acceso.OperadorId, forzarFallo));
        else
            receptor.CambiarFallo(forzarFallo);
        await _contexto.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }
}