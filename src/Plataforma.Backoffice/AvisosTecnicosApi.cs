using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Avisos;
using Plataforma.Domain;
using Plataforma.Infrastructure;
using AvisoRecibido = Plataforma.Avisos.AvisoComercio;
using RegistroAvisoComercio = Plataforma.Domain.AvisoComercio;

namespace Plataforma.Backoffice;

public static class AvisosTecnicosApi
{
    public static IEndpointRouteBuilder MapAvisosTecnicos(this IEndpointRouteBuilder app)
    {
        // Receptor de prueba "del comercio" (letra 6.9). Se configura como URL de la suscripción en el Portal.
        // Como el comercio real, verifica la firma con el secreto que el comercio cargó en su suscripción
        // (si no hay, usa Avisos:ClaveFirma). Con "Receptor: fallando" responde 503 para demostrar
        // reintentos, cola de fallidos y recuperación.
        app.MapPost("/api/receptor-prueba/avisos", async (
            HttpRequest request,
            PlataformaDbContext contexto,
            ServicioAvisos avisos,
            IConfiguration configuracion,
            CancellationToken cancellationToken) =>
        {
            var json = await new StreamReader(request.Body).ReadToEndAsync(cancellationToken);

            AvisoRecibido? aviso;
            try
            {
                aviso = System.Text.Json.JsonSerializer.Deserialize<AvisoRecibido>(json, JsonAvisos.Opciones);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { mensaje = "Aviso inválido." });
            }
            if (aviso is null || aviso.EnvioId == Guid.Empty)
                return Results.BadRequest(new { mensaje = "Aviso inválido." });

            // El aviso no trae el operador (no se le exponen ids internos al comercio): se resuelve por el envío.
            var envio = await contexto.Envios.IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.Id == aviso.EnvioId, cancellationToken);
            if (envio is null)
                return Results.BadRequest(new { mensaje = "El envío del aviso no existe." });

            // Receptor "caído": responde 503 sin procesar, como un servidor fuera de servicio.
            var configuracionReceptor = await contexto.ReceptoresPruebaConfiguracion
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.OperadorId == envio.OperadorId, cancellationToken);
            if (configuracionReceptor?.ForzarFallo == true)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var suscripcion = await avisos.ObtenerSuscripcionAsync(envio.OperadorId, envio.ComercioId, cancellationToken);
            var clave = suscripcion?.Secreto ?? configuracion["Avisos:ClaveFirma"];
            if (string.IsNullOrWhiteSpace(clave) || !FirmaAviso.Verificar(
                    clave,
                    request.Headers[FirmaAviso.HeaderTimestamp],
                    json,
                    request.Headers[FirmaAviso.HeaderFirma],
                    DateTimeOffset.UtcNow))
                return Results.Unauthorized();

            contexto.AvisosComercio.Add(RegistroAvisoComercio.Crear(
                Guid.NewGuid(), envio.OperadorId, envio.ComercioId,
                $"Envío {aviso.EnvioId}: {aviso.EstadoAnterior} -> {aviso.EstadoNuevo} (evento {aviso.EventId})", DateTime.UtcNow));
            await contexto.SaveChangesAsync(cancellationToken);

            // El tablero en vivo no depende del receptor: lo alimenta TableroConsumidor desde la cola.
            return Results.Ok();
        }).AllowAnonymous();

        var tecnico = app.MapGroup("/api/tecnico/avisos").RequireAuthorization();
        tecnico.MapGet("/fallidos", async (ClaimsPrincipal principal, ServicioAvisos avisos, CancellationToken cancellationToken) =>
        {
            if (!PuedeOperar(principal, out var acceso)) return Results.Forbid();
            var fallidos = await avisos.ListarEntregasOperadorAsync(acceso.OperadorId, EstadoEntrega.Fallido, 200, cancellationToken);
            return Results.Ok(fallidos.Select(aviso => new
            {
                aviso.Id, aviso.EventId, aviso.EnvioId, Error = aviso.UltimoError, aviso.Intentos, CreadoEn = aviso.CreadaEn, ActualizadoEn = aviso.ActualizadaEn
            }));
        });

        tecnico.MapPost("/fallidos/{id:guid}/reintentar", async (
            Guid id,
            ClaimsPrincipal principal,
            ServicioAvisos avisos,
            CancellationToken cancellationToken) =>
        {
            if (!PuedeOperar(principal, out var acceso)) return Results.Forbid();
            try
            {
                // Reintenta solo esa entrega con el mismo EventId; no republica el evento a los demás consumidores.
                await avisos.ReintentarAsync(id, acceso.OperadorId, cancellationToken: cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
            return Results.Accepted($"/api/tecnico/avisos/fallidos/{id}");
        });

        tecnico.MapPut("/receptor-prueba/fallar", async (
            ReceptorPruebaSolicitud solicitud,
            ClaimsPrincipal principal,
            PlataformaDbContext contexto,
            CancellationToken cancellationToken) =>
        {
            if (!PuedeOperar(principal, out var acceso)) return Results.Forbid();
            await CambiarReceptorAsync(contexto, acceso.OperadorId, solicitud.ForzarFallo, cancellationToken);
            return Results.NoContent();
        });

        return app;
    }

    /// <summary>Activa o desactiva la caída simulada del receptor de prueba del operador.</summary>
    public static async Task CambiarReceptorAsync(
        PlataformaDbContext contexto,
        Guid operadorId,
        bool forzarFallo,
        CancellationToken cancellationToken)
    {
        var receptor = await contexto.ReceptoresPruebaConfiguracion
            .SingleOrDefaultAsync(item => item.OperadorId == operadorId, cancellationToken);
        if (receptor is null)
            contexto.ReceptoresPruebaConfiguracion.Add(ReceptorPruebaConfiguracion.Crear(operadorId, forzarFallo));
        else
            receptor.CambiarFallo(forzarFallo);
        await contexto.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Mensajes fallidos y receptor de prueba: el administrador (letra 6.8).</summary>
    private static bool PuedeOperar(ClaimsPrincipal principal, out AccesoActual acceso) =>
        AccesoActual.Intentar(principal, out acceso) && acceso.Rol == Roles.Administrador;
}

public sealed record ReceptorPruebaSolicitud(bool ForzarFallo);
