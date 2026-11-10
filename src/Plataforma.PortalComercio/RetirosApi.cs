using System.Security.Claims;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.PortalComercio;

/// <summary>API de retiros para el comercio (RF 37–41). El tablero/despacho queda en Backoffice.</summary>
public static class RetirosApi
{
    public static IEndpointRouteBuilder MapApisRetirosComercio(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/retiros", async (
            ClaimsPrincipal principal,
            RetiroPortalSolicitud solicitud,
            GestionRetiros retiros,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol != Roles.Comercio || acceso.ComercioId is not Guid comercioId)
                return Results.Unauthorized();

            try
            {
                var r = await retiros.SolicitarAsync(
                    acceso.OperadorId,
                    comercioId,
                    solicitud.Direccion,
                    solicitud.Contacto ?? "",
                    solicitud.Fecha,
                    solicitud.Observaciones ?? "",
                    ct);
                return Results.Json(new { r.Id, r.Estado, r.FechaSolicitada }, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapGet("/api/retiros", async (
            ClaimsPrincipal principal,
            GestionRetiros retiros,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol != Roles.Comercio || acceso.ComercioId is not Guid comercioId)
                return Results.Unauthorized();

            var lista = await retiros.ListarAsync(acceso.OperadorId, comercioId, ct);
            return Results.Json(lista, ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapPost("/api/retiros/{retiroId:guid}/cancelar", async (
            Guid retiroId,
            ClaimsPrincipal principal,
            CancelarRetiroPortalSolicitud? solicitud,
            GestionRetiros retiros,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol != Roles.Comercio || acceso.ComercioId is not Guid comercioId)
                return Results.Unauthorized();

            var lista = await retiros.ListarAsync(acceso.OperadorId, comercioId, ct);
            if (lista.All(r => r.Id != retiroId))
                return Results.NotFound();

            try
            {
                await retiros.CancelarAsync(acceso.OperadorId, retiroId, solicitud?.Motivo ?? "", ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        return app;
    }
}

public sealed record RetiroPortalSolicitud(
    string Direccion,
    DateOnly Fecha,
    string? Contacto = null,
    string? Observaciones = null);

public sealed record CancelarRetiroPortalSolicitud(string? Motivo = null);
