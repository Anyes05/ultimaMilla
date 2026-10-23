using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Application.Mensajes;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Backoffice;

public static class AvisosTecnicosApi
{
    public static IEndpointRouteBuilder MapAvisosTecnicos(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/receptor-prueba/avisos", async (
            HttpRequest request,
            PlataformaDbContext contexto,
            IHubContext<HubAvisosOperador> hub,
            IConfiguration configuracion,
            CancellationToken cancellationToken) =>
        {
            var json = await new StreamReader(request.Body).ReadToEndAsync(cancellationToken);
            var clave = configuracion["Avisos:ClaveFirma"];
            var firma = request.Headers["X-Plataforma-Firma"].ToString();
            if (string.IsNullOrWhiteSpace(clave) || !FirmaValida(clave, json, firma))
                return Results.Unauthorized();

            EnvioEstadoCambiado? evento;
            try
            {
                evento = EnvioEstadoCambiado.Deserializar(json);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { mensaje = "Evento inválido." });
            }
            if (evento is null)
                return Results.BadRequest(new { mensaje = "Evento inválido." });

            var configuracionReceptor = await contexto.ReceptoresPruebaConfiguracion
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.OperadorId == evento.OperadorId, cancellationToken);
            if (configuracionReceptor?.ForzarFallo == true)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var envio = await contexto.Envios.IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.Id == evento.EnvioId && item.OperadorId == evento.OperadorId, cancellationToken);
            if (envio is null)
                return Results.BadRequest(new { mensaje = "El envío no pertenece al operador del evento." });

            contexto.AvisosComercio.Add(AvisoComercio.Crear(
                Guid.NewGuid(), evento.OperadorId, envio.ComercioId,
                $"Envío {evento.EnvioId}: {evento.EstadoAnterior} -> {evento.EstadoNuevo}", DateTime.UtcNow));
            await contexto.SaveChangesAsync(cancellationToken);

            await hub.Clients.Group(HubAvisosOperador.Grupo(evento.OperadorId)).SendAsync(
                "estadoEnvioCambiado",
                new { evento.EventId, evento.EnvioId, evento.EstadoAnterior, evento.EstadoNuevo, evento.Fecha },
                cancellationToken);
            return Results.Ok();
        }).AllowAnonymous();

        var tecnico = app.MapGroup("/api/tecnico/avisos").RequireAuthorization();
        tecnico.MapGet("/fallidos", async (ClaimsPrincipal principal, PlataformaDbContext contexto, CancellationToken cancellationToken) =>
        {
            if (!PuedeOperar(principal, out _)) return Results.Forbid();
            var fallidos = await contexto.AvisosFallidos
                .Where(aviso => aviso.ResueltoEn == null)
                .OrderByDescending(aviso => aviso.ActualizadoEn)
                .Select(aviso => new { aviso.Id, aviso.EventId, aviso.EnvioId, aviso.Error, aviso.Intentos, aviso.CreadoEn, aviso.ActualizadoEn })
                .ToListAsync(cancellationToken);
            return Results.Ok(fallidos);
        });

        tecnico.MapPost("/fallidos/{id:guid}/reintentar", async (
            Guid id,
            ClaimsPrincipal principal,
            PlataformaDbContext contexto,
            IPublicadorMensajes publicador,
            CancellationToken cancellationToken) =>
        {
            if (!PuedeOperar(principal, out _)) return Results.Forbid();
            var fallido = await contexto.AvisosFallidos.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (fallido is null || fallido.ResueltoEn is not null) return Results.NotFound();
            var evento = EnvioEstadoCambiado.Deserializar(fallido.Payload);
            if (evento is null) return Results.BadRequest(new { mensaje = "El payload del aviso es inválido." });
            await publicador.PublicarAsync(EnvioEstadoCambiado.Tipo, EnvioEstadoCambiado.Version, evento.EventId, fallido.Payload, cancellationToken);
            return Results.Accepted($"/api/tecnico/avisos/fallidos/{id}");
        });

        tecnico.MapPut("/receptor-prueba/fallar", async (
            ReceptorPruebaSolicitud solicitud,
            ClaimsPrincipal principal,
            PlataformaDbContext contexto,
            CancellationToken cancellationToken) =>
        {
            if (!PuedeOperar(principal, out var acceso)) return Results.Forbid();
            var receptor = await contexto.ReceptoresPruebaConfiguracion.SingleOrDefaultAsync(item => item.OperadorId == acceso.OperadorId, cancellationToken);
            if (receptor is null)
                contexto.ReceptoresPruebaConfiguracion.Add(ReceptorPruebaConfiguracion.Crear(acceso.OperadorId, solicitud.ForzarFallo));
            else
                receptor.CambiarFallo(solicitud.ForzarFallo);
            await contexto.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        return app;
    }

    private static bool PuedeOperar(ClaimsPrincipal principal, out AccesoActual acceso) =>
        AccesoActual.Intentar(principal, out acceso) && acceso.Rol is Roles.Administrador or Roles.Despachador or Roles.OperarioDeposito;

    private static bool FirmaValida(string clave, string json, string firma)
    {
        var esperada = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(clave), Encoding.UTF8.GetBytes(json)));
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(esperada), Encoding.UTF8.GetBytes(firma));
    }
}

public sealed record ReceptorPruebaSolicitud(bool ForzarFallo);