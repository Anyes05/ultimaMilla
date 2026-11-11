using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice;

/// <summary>RF 23: la app del repartidor reporta su posición durante la jornada.</summary>
public static class PosicionesApi
{
    public static IEndpointRouteBuilder MapApisPosiciones(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/repartidor/posiciones", async (
            ClaimsPrincipal principal,
            PosicionesSolicitudApi solicitud,
            RegistrarPosiciones registrar,
            IHubContext<HubAvisosOperador> hub,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol != Roles.Repartidor ||
                acceso.RepartidorId is not Guid repartidorId)
                return Results.Unauthorized();

            try
            {
                var resultado = await registrar.EjecutarAsync(acceso.OperadorId, repartidorId, solicitud.Posiciones ?? [], ct);
                // La posición es telemetría efímera: avisa al Tablero del operador para que redibuje la flota.
                if (resultado.Aceptadas > 0)
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

public sealed record PosicionesSolicitudApi(IReadOnlyList<PosicionReportada>? Posiciones);
