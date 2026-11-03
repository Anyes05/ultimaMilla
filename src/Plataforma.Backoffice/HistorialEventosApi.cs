using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Plataforma.Application;
using Plataforma.HistorialEventos;

namespace Plataforma.Backoffice;

public static class HistorialEventosApi
{
    public static IEndpointRouteBuilder MapHistorialEventos(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/historial/eventos", async (Guid? envioId, string? estado, DateTimeOffset? desde, DateTimeOffset? hasta,
            string? despuesDe, int? tamano, ClaimsPrincipal principal, RepositorioHistorialMongo repositorio, CancellationToken cancellationToken) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso)) return Results.Forbid();
            if (!repositorio.Configurado) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            try
            {
                var pagina = await repositorio.ConsultarAsync(new ConsultaHistorialEventos(acceso.OperadorId, envioId, estado, desde, hasta, despuesDe, tamano ?? 50), cancellationToken);
                return Results.Ok(new { items = pagina.Items, siguiente = pagina.Siguiente });
            }
            catch (ArgumentException) { return Results.BadRequest(new { mensaje = "El cursor despuesDe es inválido." }); }
        }).RequireAuthorization(new AuthorizeAttribute { Roles = PermisosPerfil.Operacion });
        return app;
    }
}