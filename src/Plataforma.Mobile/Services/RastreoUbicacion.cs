using Microsoft.Extensions.Logging;

namespace Plataforma.Mobile.Services;

public interface IRastreoUbicacion
{
    Task ActualizarAsync(bool permitirInicio = true);
    Task EnviarPendientesAsync();
}

public sealed class RastreoUbicacion(RepartidorHttpService repartidor,
    ILogger<RastreoUbicacion> logger) : IRastreoUbicacion
{
    private bool actualizando;
    private bool permisoRechazado;

    public async Task ActualizarAsync(bool permitirInicio = true)
    {
        if (actualizando) return;
        actualizando = true;
        try
        {
#if ANDROID
            var contexto = Android.App.Application.Context;
            var intent = new Android.Content.Intent(contexto, typeof(Platforms.Android.ServicioUbicacion));
            if (!repartidor.DebeRastrearUbicacion)
            {
                contexto.StopService(intent);
                await EnviarPendientesAsync();
                return;
            }
            if (!permitirInicio || Platforms.Android.ServicioUbicacion.Activo || Platform.CurrentActivity is null)
                return;
            var permiso = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (permiso != PermissionStatus.Granted)
            {
                if (permisoRechazado) return;
                permiso = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }
            if (permiso != PermissionStatus.Granted)
            {
                permisoRechazado = true;
                await Shell.Current.DisplayAlertAsync("Ubicacion de la jornada",
                    "Permite la ubicacion desde Ajustes para compartir tu posicion durante el reparto.", "Aceptar");
                return;
            }
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                await Permissions.RequestAsync<PermisoNotificaciones>();
            if (!repartidor.DebeRastrearUbicacion) return;
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) contexto.StartForegroundService(intent);
            else contexto.StartService(intent);
#endif
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo actualizar el rastreo de ubicacion");
        }
        finally { actualizando = false; }
    }

    public async Task EnviarPendientesAsync()
    {
        try { await repartidor.EnviarPosicionesAsync(); }
        catch (Exception ex) { logger.LogWarning(ex, "Posiciones conservadas para el siguiente envio"); }
    }

#if ANDROID
    public sealed class PermisoNotificaciones : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            OperatingSystem.IsAndroidVersionAtLeast(33)
                ? [(Android.Manifest.Permission.PostNotifications, true)]
                : [];
    }
#endif
}