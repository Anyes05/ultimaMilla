using System.Security.Claims;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice;

/// <summary>APIs del núcleo Ana: banderas, retiros, recepción, cobertura.</summary>
public static class NucleoAnaApi
{
    public static IEndpointRouteBuilder MapApisNucleoAna(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/plataforma/banderas", async (
            ClaimsPrincipal principal,
            GestionBanderas banderas,
            IOperadorRepositorio operadores,
            CancellationToken ct) =>
        {
            // Banderas: solo quien ofrece la plataforma (aclaración 3.2), no el administrador de un operador.
            if (!AccesoActual.Intentar(principal, out var acceso))
                return Results.Unauthorized();
            if (!acceso.EsAdministradorPlataforma)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var ops = await operadores.ListarIdsAsync(ct);
            var lista = await banderas.ListarTodasAsync(ops, ct);
            return Results.Json(lista, ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapPost("/api/plataforma/banderas", async (
            ClaimsPrincipal principal,
            CambioBanderaSolicitud solicitud,
            GestionBanderas banderas,
            CancellationToken ct) =>
        {
            // Banderas: solo quien ofrece la plataforma (aclaración 3.2), no el administrador de un operador.
            if (!AccesoActual.Intentar(principal, out var acceso))
                return Results.Unauthorized();
            if (!acceso.EsAdministradorPlataforma)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                await banderas.CambiarAsync(
                    solicitud.OperadorId,
                    solicitud.Clave,
                    solicitud.Activa,
                    acceso.UsuarioId.ToString(),
                    ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapGet("/api/plataforma/banderas/historial", async (
            ClaimsPrincipal principal,
            Guid? operadorId,
            GestionBanderas banderas,
            CancellationToken ct) =>
        {
            // Banderas: solo quien ofrece la plataforma (aclaración 3.2), no el administrador de un operador.
            if (!AccesoActual.Intentar(principal, out var acceso))
                return Results.Unauthorized();
            if (!acceso.EsAdministradorPlataforma)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var hist = await banderas.HistorialAsync(operadorId, ct);
            return Results.Json(hist, ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapPost("/api/deposito/recepcion", async (
            ClaimsPrincipal principal,
            RecepcionSolicitudApi solicitud,
            RecibirEnvioEnDeposito recibir,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol is not (Roles.OperarioDeposito or Roles.Administrador))
                return Results.Unauthorized();

            try
            {
                var r = await recibir.EjecutarAsync(
                    acceso.OperadorId,
                    solicitud.EnvioId,
                    solicitud.BultosRecibidos,
                    acceso.UsuarioId.ToString(),
                    solicitud.Observaciones ?? "",
                    ct);
                return Results.Json(r, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPost("/api/retiros", async (
            ClaimsPrincipal principal,
            RetiroSolicitudApi solicitud,
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
                return Results.Json(new { r.Id, r.Estado, r.FechaSolicitada, r.TarifaCalculada }, ObtenerRutasDeHoy.OpcionesJson);
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
            if (!AccesoActual.Intentar(principal, out var acceso))
                return Results.Unauthorized();

            Guid? comercio = acceso.Rol == Roles.Comercio ? acceso.ComercioId : null;
            var lista = await retiros.ListarAsync(acceso.OperadorId, comercio, ct);
            return Results.Json(lista, ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapPost("/api/retiros/{retiroId:guid}/programar", async (
            Guid retiroId,
            ClaimsPrincipal principal,
            ProgramarRetiroSolicitud solicitud,
            GestionRetiros retiros,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol is not (Roles.Despachador or Roles.Administrador))
                return Results.Unauthorized();

            try
            {
                await retiros.ProgramarAsync(acceso.OperadorId, retiroId, solicitud.RutaId, ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPost("/api/retiros/{retiroId:guid}/completar", async (
            Guid retiroId,
            ClaimsPrincipal principal,
            CompletarRetiroSolicitud? solicitud,
            GestionRetiros retiros,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol is not (Roles.Despachador or Roles.Administrador or Roles.OperarioDeposito))
                return Results.Unauthorized();

            try
            {
                await retiros.CompletarAsync(acceso.OperadorId, retiroId, solicitud?.EnvioAsociadoId, ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPost("/api/retiros/{retiroId:guid}/cancelar", async (
            Guid retiroId,
            ClaimsPrincipal principal,
            CancelarRetiroSolicitud? solicitud,
            GestionRetiros retiros,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso))
                return Results.Unauthorized();

            if (acceso.Rol is not (Roles.Administrador or Roles.Despachador or Roles.Comercio))
                return Results.Unauthorized();

            try
            {
                if (acceso.Rol == Roles.Comercio)
                {
                    var lista = await retiros.ListarAsync(acceso.OperadorId, acceso.ComercioId, ct);
                    if (lista.All(r => r.Id != retiroId))
                        return Results.Unauthorized();
                }

                await retiros.CancelarAsync(acceso.OperadorId, retiroId, solicitud?.Motivo ?? "", ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapGet("/api/cobertura/zonas", async (
            ClaimsPrincipal principal, GestionCobertura cobertura, CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso))
                return Results.Unauthorized();
            return Results.Json(await cobertura.ListarZonasAsync(acceso.OperadorId, ct), ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapPost("/api/cobertura/zonas", async (
            ClaimsPrincipal principal, ZonaSolicitud solicitud, GestionCobertura cobertura, CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                var z = await cobertura.DefinirZonaAsync(acceso.OperadorId, solicitud.Nombre, solicitud.Poligono ?? "[]", ct);
                return Results.Json(z, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPut("/api/cobertura/zonas/{zonaId:guid}", async (
            Guid zonaId,
            ClaimsPrincipal principal,
            ZonaSolicitud solicitud,
            GestionCobertura cobertura,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                await cobertura.ActualizarZonaAsync(acceso.OperadorId, zonaId, solicitud.Nombre, solicitud.Poligono ?? "[]", ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapGet("/api/cobertura/franjas", async (
            ClaimsPrincipal principal, GestionCobertura cobertura, CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso))
                return Results.Unauthorized();
            return Results.Json(await cobertura.ListarFranjasAsync(acceso.OperadorId, ct), ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapPost("/api/cobertura/franjas", async (
            ClaimsPrincipal principal, FranjaSolicitud solicitud, GestionCobertura cobertura, CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                var f = await cobertura.DefinirFranjaAsync(
                    acceso.OperadorId, solicitud.Nombre, solicitud.Desde, solicitud.Hasta, solicitud.ZonaId, ct);
                return Results.Json(f, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPut("/api/cobertura/franjas/{franjaId:guid}", async (
            Guid franjaId,
            ClaimsPrincipal principal,
            FranjaActualizarSolicitud solicitud,
            GestionCobertura cobertura,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                await cobertura.ActualizarFranjaAsync(
                    acceso.OperadorId, franjaId, solicitud.Nombre, solicitud.Desde, solicitud.Hasta,
                    solicitud.ZonaId, solicitud.Activa, ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapGet("/api/cobertura/vehiculos", async (
            ClaimsPrincipal principal, GestionCobertura cobertura, CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso))
                return Results.Unauthorized();
            return Results.Json(await cobertura.ListarVehiculosAsync(acceso.OperadorId, ct), ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapPost("/api/cobertura/vehiculos", async (
            ClaimsPrincipal principal, VehiculoSolicitud solicitud, GestionCobertura cobertura, CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol is not (Roles.Administrador or Roles.Despachador))
                return Results.Unauthorized();
            try
            {
                var v = await cobertura.RegistrarVehiculoAsync(
                    acceso.OperadorId,
                    solicitud.Patente,
                    solicitud.Descripcion ?? "",
                    solicitud.CapacidadPesoKg,
                    solicitud.CapacidadVolumenCm3,
                    solicitud.MaximoParadas,
                    ct);
                return Results.Json(v, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPut("/api/cobertura/vehiculos/{vehiculoId:guid}", async (
            Guid vehiculoId,
            ClaimsPrincipal principal,
            VehiculoActualizarSolicitud solicitud,
            GestionCobertura cobertura,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol is not (Roles.Administrador or Roles.Despachador))
                return Results.Unauthorized();
            try
            {
                await cobertura.ActualizarVehiculoAsync(
                    acceso.OperadorId,
                    vehiculoId,
                    solicitud.CapacidadPesoKg,
                    solicitud.CapacidadVolumenCm3,
                    solicitud.MaximoParadas,
                    solicitud.Activo,
                    ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapGet("/api/comercios", async (
            ClaimsPrincipal principal, GestionComercios comercios, CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            return Results.Json(await comercios.ListarAsync(acceso.OperadorId, ct), ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapPost("/api/comercios", async (
            ClaimsPrincipal principal, ComercioSolicitudApi solicitud, GestionComercios comercios, CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                var c = await comercios.RegistrarAsync(
                    acceso.OperadorId,
                    solicitud.NombreComercial,
                    solicitud.RazonSocial ?? "",
                    solicitud.DocumentoFiscal ?? "",
                    solicitud.DireccionPrincipal ?? "",
                    ct);
                return Results.Json(c, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPut("/api/comercios/{comercioId:guid}", async (
            Guid comercioId,
            ClaimsPrincipal principal,
            ComercioSolicitudApi solicitud,
            GestionComercios comercios,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                await comercios.ActualizarAsync(
                    acceso.OperadorId,
                    comercioId,
                    solicitud.NombreComercial,
                    solicitud.RazonSocial ?? "",
                    solicitud.DocumentoFiscal ?? "",
                    solicitud.DireccionPrincipal ?? "",
                    ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPost("/api/comercios/{comercioId:guid}/activo", async (
            Guid comercioId,
            ClaimsPrincipal principal,
            ComercioActivoSolicitud solicitud,
            GestionComercios comercios,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                await comercios.CambiarActivoAsync(acceso.OperadorId, comercioId, solicitud.Activo, ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPost("/api/deposito/rendicion/bultos", async (
            ClaimsPrincipal principal,
            RendicionBultoSolicitud solicitud,
            RendirBulto rendir,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol is not (Roles.OperarioDeposito or Roles.Administrador))
                return Results.Unauthorized();
            try
            {
                ResultadoRendicionBulto r;
                if (solicitud.BultoId is Guid id)
                    r = await rendir.PorIdAsync(acceso.OperadorId, id, acceso.UsuarioId.ToString(), solicitud.Motivo ?? "Rendición en depósito", ct);
                else if (!string.IsNullOrWhiteSpace(solicitud.Codigo))
                    r = await rendir.PorCodigoAsync(acceso.OperadorId, solicitud.Codigo, acceso.UsuarioId.ToString(), solicitud.Motivo ?? "Rendición en depósito", ct);
                else
                    return Results.BadRequest(new { mensaje = "Indicá codigo o bultoId." });
                return Results.Json(r, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapGet("/api/deposito/rendicion/bultos", async (
            ClaimsPrincipal principal,
            IBultoRepositorio bultos,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) ||
                acceso.Rol is not (Roles.OperarioDeposito or Roles.Administrador))
                return Results.Unauthorized();
            return Results.Json(await bultos.ListarPendientesRendicionAsync(acceso.OperadorId, ct), ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapPost("/api/envios", async (
            ClaimsPrincipal principal,
            AdmitirEnvioSolicitudApi solicitud,
            AdmitirEnvio admitir,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso))
                return Results.Unauthorized();

            var comercioId = acceso.Rol == Roles.Comercio
                ? acceso.ComercioId
                : solicitud.ComercioId;
            if (comercioId is not Guid cId)
                return Results.BadRequest(new { mensaje = "El comercio es requerido." });
            if (acceso.Rol is not (Roles.Comercio or Roles.Administrador or Roles.OperarioDeposito))
                return Results.Unauthorized();

            try
            {
                var envio = await admitir.EjecutarAsync(
                    acceso.OperadorId,
                    cId,
                    solicitud.Destinatario,
                    solicitud.DireccionEntrega,
                    solicitud.EmailDestinatario ?? "",
                    solicitud.PesoKg,
                    solicitud.DistanciaKm,
                    solicitud.Fragil,
                    solicitud.Urgente,
                    solicitud.CantidadBultos < 1 ? 1 : solicitud.CantidadBultos,
                    solicitud.ZonaId,
                    solicitud.FranjaId,
                    solicitud.VolumenCm3,
                    solicitud.ClaveExterna,
                    ct);
                return Results.Json(new
                {
                    envio.Id,
                    envio.NumeroSeguimiento,
                    envio.Estado,
                    envio.TarifaCalculada,
                    envio.ZonaId,
                    envio.FranjaId,
                    envio.ClaveExterna
                }, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPost("/api/envios/importar", async (
            ClaimsPrincipal principal,
            ImportarEnviosSolicitudApi solicitud,
            ImportarEnvios importar,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso))
                return Results.Unauthorized();
            var comercioId = acceso.Rol == Roles.Comercio ? acceso.ComercioId : solicitud.ComercioId;
            if (comercioId is not Guid cId)
                return Results.BadRequest(new { mensaje = "El comercio es requerido." });
            if (acceso.Rol is not (Roles.Comercio or Roles.Administrador))
                return Results.Unauthorized();
            try
            {
                var r = await importar.EjecutarAsync(acceso.OperadorId, cId, solicitud.Items, ct);
                return Results.Json(r, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapGet("/api/reportes/resumen", async (
            ClaimsPrincipal principal,
            DateTime? desde,
            DateTime? hasta,
            Guid? comercioId,
            GestionLiquidaciones liquidaciones,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            var resumen = await liquidaciones.ResumenAsync(acceso.OperadorId, desde, hasta, comercioId, ct);
            return Results.Json(resumen, ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapGet("/api/liquidaciones", async (
            ClaimsPrincipal principal,
            Guid? comercioId,
            GestionLiquidaciones liquidaciones,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            return Results.Json(await liquidaciones.ListarAsync(acceso.OperadorId, comercioId, ct), ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapPost("/api/liquidaciones", async (
            ClaimsPrincipal principal,
            LiquidacionAbrirSolicitud solicitud,
            GestionLiquidaciones liquidaciones,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                var l = await liquidaciones.AbrirAsync(
                    acceso.OperadorId, solicitud.ComercioId, solicitud.PeriodoDesde, solicitud.PeriodoHasta, ct);
                return Results.Json(l, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPost("/api/liquidaciones/{liquidacionId:guid}/cerrar", async (
            Guid liquidacionId,
            ClaimsPrincipal principal,
            GestionLiquidaciones liquidaciones,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                await liquidaciones.CerrarAsync(acceso.OperadorId, liquidacionId, ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPut("/api/operador/marca", async (
            ClaimsPrincipal principal,
            MarcaOperadorSolicitud solicitud,
            ActualizarMarcaOperador actualizar,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                await actualizar.EjecutarAsync(acceso.OperadorId, solicitud.ColorMarca, ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapGet("/api/usuarios", async (
            ClaimsPrincipal principal, GestionUsuarios usuarios, CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            var lista = await usuarios.ListarAsync(acceso.OperadorId, ct);
            return Results.Json(lista.Select(u => new
            {
                u.Id,
                u.Nombre,
                u.Email,
                u.NombreUsuario,
                Rol = Roles.De(u.Rol),
                u.ComercioId,
                u.Activo
            }), ObtenerRutasDeHoy.OpcionesJson);
        }).RequireAuthorization();

        app.MapPost("/api/usuarios", async (
            ClaimsPrincipal principal, UsuarioSolicitudApi solicitud, GestionUsuarios usuarios, CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                if (!IntentarParsearRol(solicitud.Rol, out var rol))
                    return Results.BadRequest(new { mensaje = "Rol inválido." });

                var u = await usuarios.RegistrarAsync(
                    acceso.OperadorId,
                    solicitud.Nombre,
                    solicitud.Email,
                    solicitud.NombreUsuario,
                    solicitud.Clave,
                    rol,
                    solicitud.ComercioId,
                    ct);
                return Results.Json(new
                {
                    u.Id,
                    u.Nombre,
                    u.Email,
                    u.NombreUsuario,
                    Rol = Roles.De(u.Rol),
                    u.ComercioId,
                    u.Activo
                }, ObtenerRutasDeHoy.OpcionesJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPut("/api/usuarios/{usuarioId:guid}", async (
            Guid usuarioId,
            ClaimsPrincipal principal,
            UsuarioActualizarSolicitud solicitud,
            GestionUsuarios usuarios,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                if (!IntentarParsearRol(solicitud.Rol, out var rol))
                    return Results.BadRequest(new { mensaje = "Rol inválido." });

                await usuarios.ActualizarAsync(
                    acceso.OperadorId,
                    usuarioId,
                    solicitud.Nombre,
                    solicitud.Email,
                    rol,
                    solicitud.ComercioId,
                    ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPost("/api/usuarios/{usuarioId:guid}/activo", async (
            Guid usuarioId,
            ClaimsPrincipal principal,
            UsuarioActivoSolicitud solicitud,
            GestionUsuarios usuarios,
            CancellationToken ct) =>
        {
            if (!AccesoActual.Intentar(principal, out var acceso) || acceso.Rol != Roles.Administrador)
                return Results.Unauthorized();
            try
            {
                await usuarios.CambiarActivoAsync(acceso.OperadorId, usuarioId, solicitud.Activo, ct);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { mensaje = ex.Message });
            }
        }).RequireAuthorization();

        return app;
    }

    private static bool IntentarParsearRol(string valor, out RolUsuario rol)
    {
        if (Enum.TryParse(valor, ignoreCase: true, out rol))
            return true;

        foreach (RolUsuario candidato in Enum.GetValues<RolUsuario>())
        {
            if (string.Equals(Roles.De(candidato), valor, StringComparison.OrdinalIgnoreCase))
            {
                rol = candidato;
                return true;
            }
        }

        rol = default;
        return false;
    }
}

public sealed record CambioBanderaSolicitud(Guid OperadorId, string Clave, bool Activa);
public sealed record RecepcionSolicitudApi(Guid EnvioId, int BultosRecibidos, string? Observaciones = null);
public sealed record RetiroSolicitudApi(string Direccion, DateOnly Fecha, string? Contacto = null, string? Observaciones = null);
public sealed record ProgramarRetiroSolicitud(Guid RutaId);
public sealed record CompletarRetiroSolicitud(Guid? EnvioAsociadoId = null);
public sealed record CancelarRetiroSolicitud(string? Motivo = null);
public sealed record ZonaSolicitud(string Nombre, string? Poligono = null);
public sealed record FranjaSolicitud(string Nombre, TimeOnly Desde, TimeOnly Hasta, Guid? ZonaId = null);
public sealed record FranjaActualizarSolicitud(
    string Nombre,
    TimeOnly Desde,
    TimeOnly Hasta,
    Guid? ZonaId = null,
    bool? Activa = null);
public sealed record VehiculoSolicitud(
    string Patente,
    string? Descripcion = null,
    decimal CapacidadPesoKg = 0,
    decimal CapacidadVolumenCm3 = 0,
    int MaximoParadas = 0);
public sealed record VehiculoActualizarSolicitud(
    decimal CapacidadPesoKg = 0,
    decimal CapacidadVolumenCm3 = 0,
    int MaximoParadas = 0,
    bool? Activo = null);
public sealed record ComercioSolicitudApi(
    string NombreComercial,
    string? RazonSocial = null,
    string? DocumentoFiscal = null,
    string? DireccionPrincipal = null);
public sealed record ComercioActivoSolicitud(bool Activo);
public sealed record UsuarioSolicitudApi(
    string Nombre,
    string Email,
    string NombreUsuario,
    string Clave,
    string Rol,
    Guid? ComercioId = null);
public sealed record UsuarioActualizarSolicitud(
    string Nombre,
    string Email,
    string Rol,
    Guid? ComercioId = null);
public sealed record UsuarioActivoSolicitud(bool Activo);
public sealed record RendicionBultoSolicitud(string? Codigo = null, Guid? BultoId = null, string? Motivo = null);
public sealed record AdmitirEnvioSolicitudApi(
    string Destinatario,
    string DireccionEntrega,
    Guid? ComercioId = null,
    string? EmailDestinatario = null,
    decimal PesoKg = 1,
    decimal DistanciaKm = 0,
    bool Fragil = false,
    bool Urgente = false,
    int CantidadBultos = 1,
    Guid? ZonaId = null,
    Guid? FranjaId = null,
    decimal VolumenCm3 = 0,
    string? ClaveExterna = null);
public sealed record ImportarEnviosSolicitudApi(Guid? ComercioId, IReadOnlyList<EnvioImportacionItem> Items);
public sealed record LiquidacionAbrirSolicitud(Guid ComercioId, DateTime PeriodoDesde, DateTime PeriodoHasta);
public sealed record MarcaOperadorSolicitud(string ColorMarca);
