using System.Security.Claims;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice;

public static class RepartidorApi
{
    public static IEndpointRouteBuilder MapApisRepartidor(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/repartidores", async (
            ClaimsPrincipal principal,
            ListarRepartidoresActivos listar,
            CancellationToken cancellationToken) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol is not (Roles.Despachador or Roles.Administrador or Roles.OperarioDeposito))
                return Results.Unauthorized();

            var lista = await listar.EjecutarAsync(acceso.OperadorId, cancellationToken);
            return Results.Json(lista, ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        var repartidor = app.MapGroup("/api/repartidor");

        repartidor.MapPost("/login", async (
            LoginRepartidorSolicitud solicitud,
            IniciarSesionRepartidor iniciar,
            CancellationToken cancellationToken) =>
        {
            var resultado = await iniciar.Ejecutar(solicitud.Usuario, solicitud.Clave, cancellationToken);
            return resultado switch
            {
                ResultadoInicioRepartidor.Ok ok => Results.Json(ok.Sesion, ObtenerRutasDeHoy.OpcionesJson),
                ResultadoInicioRepartidor.CredencialesInvalidas => Results.Json(
                    new { mensaje = "Credenciales inválidas." }, statusCode: StatusCodes.Status401Unauthorized),
                ResultadoInicioRepartidor.RolNoPermitido => Results.Json(
                    new { mensaje = "El usuario no tiene rol de repartidor." }, statusCode: StatusCodes.Status403Forbidden),
                ResultadoInicioRepartidor.SinPerfil => Results.Json(
                    new { mensaje = "No hay perfil de repartidor activo." }, statusCode: StatusCodes.Status403Forbidden),
                ResultadoInicioRepartidor.DemasiadosIntentos => Results.Json(
                    new { mensaje = "Demasiados intentos. Probá de nuevo en unos minutos." },
                    statusCode: StatusCodes.Status429TooManyRequests),
                _ => Results.Json(new { mensaje = "No se pudo iniciar sesión." }, statusCode: StatusCodes.Status401Unauthorized)
            };
        });

        repartidor.MapGet("/rutas/hoy", async (
            ClaimsPrincipal principal,
            ObtenerRutasDeHoy obtener,
            IOperadorRepositorio operadores,
            DateOnly? fecha,
            CancellationToken cancellationToken) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol != Roles.Repartidor ||
                acceso.RepartidorId is not Guid repartidorId)
                return Results.Unauthorized();

            DateOnly dia;
            if (fecha is DateOnly indicada)
                dia = indicada;
            else
            {
                var operador = await operadores.ObtenerAsync(acceso.OperadorId, cancellationToken);
                dia = operador?.DiaActual() ?? DateOnly.FromDateTime(DateTime.UtcNow);
            }

            var rutas = await obtener.Ejecutar(acceso.OperadorId, repartidorId, dia, cancellationToken);
            return Results.Json(rutas, ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        repartidor.MapPost("/sincronizacion", async (
            ClaimsPrincipal principal,
            SincronizacionSolicitudApi solicitud,
            SincronizarEntregas sincronizar,
            CancellationToken cancellationToken) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol != Roles.Repartidor ||
                acceso.RepartidorId is not Guid repartidorId)
                return Results.Unauthorized();

            try
            {
                var usuario = principal.Identity?.Name ?? acceso.UsuarioId.ToString();
                var resultado = await sincronizar.Ejecutar(
                    acceso.OperadorId,
                    repartidorId,
                    solicitud.Fecha,
                    solicitud.Eventos,
                    usuario,
                    cancellationToken);
                return Results.Json(resultado, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new { mensaje = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization();

        repartidor.MapPost("/dispositivos", async (
            ClaimsPrincipal principal,
            DispositivoSolicitud solicitud,
            RegistrarDispositivoRepartidor registrar,
            CancellationToken cancellationToken) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol != Roles.Repartidor ||
                acceso.RepartidorId is not Guid repartidorId)
                return Results.Unauthorized();

            try
            {
                var dispositivo = await registrar.EjecutarAsync(
                    acceso.OperadorId,
                    repartidorId,
                    solicitud.Token,
                    solicitud.Plataforma,
                    cancellationToken);
                return Results.Json(
                    new
                    {
                        dispositivo.Id,
                        dispositivo.Token,
                        dispositivo.Plataforma,
                        dispositivo.RegistradoEn
                    },
                    ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new { mensaje = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization();

        repartidor.MapDelete("/dispositivos/{token}", async (
            ClaimsPrincipal principal,
            string token,
            QuitarDispositivoRepartidor quitar,
            CancellationToken cancellationToken) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol != Roles.Repartidor ||
                acceso.RepartidorId is not Guid repartidorId)
                return Results.Unauthorized();

            var ok = await quitar.EjecutarAsync(acceso.OperadorId, repartidorId, token, cancellationToken);
            return ok ? Results.NoContent() : Results.NotFound(new { mensaje = "Dispositivo no registrado." });
        }).RequireAuthorization();

        app.MapPost("/api/despacho", async (
            ClaimsPrincipal principal,
            DespachoSolicitud solicitud,
            DespacharRuta despachar,
            CancellationToken cancellationToken) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol is not (Roles.Despachador or Roles.Administrador or Roles.OperarioDeposito))
                return Results.Unauthorized();

            try
            {
                var ruta = await despachar.EjecutarAsync(
                    acceso.OperadorId,
                    solicitud.RepartidorId,
                    solicitud.Fecha,
                    solicitud.EnvioIds,
                    cancellationToken);
                return Results.Json(new { ruta.Id, ruta.Fecha, ruta.RepartidorId }, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.Json(new { mensaje = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization();

        app.MapGet("/api/seguimiento/{token}", async (
            string token,
            ObtenerSeguimientoEnvio seguimiento,
            CancellationToken cancellationToken) =>
        {
            var visible = await seguimiento.PorTokenAsync(token, cancellationToken);
            return visible is null
                ? Results.NotFound(new { mensaje = "Seguimiento no encontrado." })
                : Results.Json(visible, ObtenerRutasDeHoy.OpcionesJson);
        });

        /// <summary>
        /// Caso real Backoffice/API: cambiar estado genera outbox + intenta publicar.
        /// Respuesta incluye eventId para verificar en RabbitMQ / MensajesOutbox.
        /// </summary>
        app.MapPost("/api/envios/{envioId:guid}/estado", async (
            Guid envioId,
            CambioEstadoSolicitud solicitud,
            ClaimsPrincipal principal,
            ITransicionEnvio transicion,
            CancellationToken cancellationToken) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol is not (Roles.Despachador or Roles.Administrador or Roles.OperarioDeposito))
                return Results.Unauthorized();

            try
            {
                var resultado = await transicion.EjecutarAsync(
                    envioId,
                    acceso.OperadorId,
                    solicitud.NuevoEstado,
                    principal.Identity?.Name,
                    solicitud.Motivo,
                    cancellationToken);

                return Results.Json(new
                {
                    resultado.EnvioId,
                    estadoAnterior = resultado.EstadoAnterior,
                    estadoNuevo = resultado.EstadoNuevo,
                    eventId = resultado.EventId
                }, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.Json(new { mensaje = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization();

        app.MapGet("/tecnico/cache", (ICacheLectura cache) =>
        {
            var stats = cache.Estadisticas();
            return Results.Json(new
            {
                hits = stats.Hits,
                misses = stats.Misses,
                ratio = stats.Ratio
            });
        });

        return app;
    }
}

public sealed record LoginRepartidorSolicitud(string Usuario, string Clave);

public sealed record SincronizacionSolicitudApi(DateOnly Fecha, IReadOnlyList<ComprobanteEntrega> Eventos);

public sealed record DespachoSolicitud(Guid RepartidorId, DateOnly Fecha, IReadOnlyList<Guid> EnvioIds);

public sealed record DispositivoSolicitud(string Token, string Plataforma);

public sealed record CambioEstadoSolicitud(EstadoEnvio NuevoEstado, string? Motivo = null);
