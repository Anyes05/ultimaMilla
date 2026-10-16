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
                _ => Results.Json(new { mensaje = "No se pudo iniciar sesión." }, statusCode: StatusCodes.Status401Unauthorized)
            };
        });

        repartidor.MapGet("/rutas/hoy", async (
            ClaimsPrincipal principal,
            ObtenerRutasDeHoy obtener,
            DateOnly? fecha,
            CancellationToken cancellationToken) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol != Roles.Repartidor ||
                acceso.RepartidorId is not Guid repartidorId)
                return Results.Unauthorized();

            var dia = fecha ?? DateOnly.FromDateTime(DateTime.UtcNow);
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
                var resultado = await sincronizar.Ejecutar(
                    acceso.OperadorId,
                    repartidorId,
                    solicitud.Fecha,
                    solicitud.Eventos,
                    cancellationToken);
                return Results.Json(resultado, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new { mensaje = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
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
