using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice;

/// <summary>RF 40: la app del repartidor registra los retiros de su ruta (bultos, foto y firma), también sin conexión.</summary>
public static class RetirosRepartidorApi
{
    public static IEndpointRouteBuilder MapApisRetirosRepartidor(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/repartidor/retiros/sincronizacion", async (
            ClaimsPrincipal principal,
            RetirosSincronizacionSolicitudApi solicitud,
            RegistrarRetirosRepartidor registrar,
            IHubContext<HubAvisosOperador> hub,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol != Roles.Repartidor ||
                acceso.RepartidorId is not Guid repartidorId)
                return Results.Unauthorized();

            try
            {
                var usuario = principal.Identity?.Name ?? acceso.UsuarioId.ToString();
                var resultado = await registrar.EjecutarAsync(
                    acceso.OperadorId, repartidorId, solicitud.Fecha, solicitud.Eventos ?? [], usuario, ct);
                // El Tablero muestra los retiros abiertos: se redibuja en vivo.
                if (resultado.Aplicados.Count > 0)
                    await hub.Clients.Group(HubAvisosOperador.Grupo(acceso.OperadorId)).SendAsync("detalleCambiado", ct);
                return Results.Json(resultado, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        return app;
    }
}

public sealed record RetirosSincronizacionSolicitudApi(DateOnly Fecha, IReadOnlyList<ComprobanteRetiroApp>? Eventos);
