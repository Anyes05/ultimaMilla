using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plataforma.Mobile.Data;
using Plataforma.Mobile.Services;

namespace Plataforma.Mobile.Platforms.Android;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeLocation)]
public sealed class ServicioUbicacion : Service, ILocationListener
{
    private const string Canal = "reparto-ubicacion";
    private LocationManager? gps;
    private System.Threading.Timer? envio;
    private RepartidorHttpService? repartidor;
    private IRastreoUbicacion? rastreo;
    private ILogger<ServicioUbicacion>? logger;
    public static bool Activo { get; private set; }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (Activo) return StartCommandResult.NotSticky;
        var servicios = IPlatformApplication.Current!.Services;
        repartidor = servicios.GetRequiredService<RepartidorHttpService>();
        rastreo = servicios.GetRequiredService<IRastreoUbicacion>();
        logger = servicios.GetRequiredService<ILogger<ServicioUbicacion>>();
        try
        {
            var administrador = (NotificationManager)GetSystemService(NotificationService)!;
            Notification.Builder aviso;
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                administrador.CreateNotificationChannel(new NotificationChannel(
                    Canal, "Ubicacion durante el reparto", NotificationImportance.Low));
                aviso = new Notification.Builder(this, Canal);
            }
            else aviso = new Notification.Builder(this);
            var abrir = PackageManager!.GetLaunchIntentForPackage(PackageName!);
            var flagsIntent = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(23)) flagsIntent |= PendingIntentFlags.Immutable;
            var pendiente = PendingIntent.GetActivity(this, 4201, abrir!,
                flagsIntent);
            var notificacion = aviso.SetContentTitle("Reparto en curso")!
                .SetContentText("Compartiendo tu ubicacion con el operador")!
                .SetSmallIcon(Resource.Mipmap.appicon)!
                .SetContentIntent(pendiente)!.SetOngoing(true)!.Build();
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(4201, notificacion, ForegroundService.TypeLocation);
            else StartForeground(4201, notificacion);
            if (!repartidor.DebeRastrearUbicacion)
            {
                StopSelf();
                return StartCommandResult.NotSticky;
            }
            gps = (LocationManager)GetSystemService(LocationService)!;
            gps.RequestLocationUpdates(LocationManager.GpsProvider, 30_000, 0, this, Looper.MainLooper!);
            Activo = true;
            envio = new System.Threading.Timer(async _ => await EnviarAsync(), null, 0, 60_000);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo iniciar el servicio GPS de reparto");
            StopSelf();
        }
        return StartCommandResult.NotSticky;
    }

    private async Task EnviarAsync()
    {
        try
        {
            if (repartidor?.DebeRastrearUbicacion != true)
            {
                StopSelf();
                return;
            }
            if (rastreo is not null) await rastreo.EnviarPendientesAsync();
        }
        catch (Exception ex) { logger?.LogWarning(ex, "No se pudo enviar la ubicacion del reparto"); }
    }

    public void OnLocationChanged(global::Android.Locations.Location location)
    {
        try
        {
            if (repartidor?.DebeRastrearUbicacion != true) { StopSelf(); return; }
            repartidor.GuardarPosicion(new PosicionPendiente(location.Latitude, location.Longitude,
                DateTimeOffset.FromUnixTimeMilliseconds(location.Time),
                location.HasAccuracy ? location.Accuracy : null));
        }
        catch (Exception ex) { logger?.LogWarning(ex, "No se pudo guardar la posicion local"); }
    }

    public void OnProviderDisabled(string provider) =>
        logger?.LogWarning("El proveedor de ubicacion de reparto esta desactivado");
    public void OnProviderEnabled(string provider) { }
    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }

    public override void OnDestroy()
    {
        Activo = false;
        envio?.Dispose();
        gps?.RemoveUpdates(this);
        if (rastreo is not null) _ = rastreo.EnviarPendientesAsync();
        base.OnDestroy();
    }
}