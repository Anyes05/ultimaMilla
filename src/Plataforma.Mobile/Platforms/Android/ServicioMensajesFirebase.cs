using Android.App;
using Android.Content;
using Firebase.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Mobile.Services;

namespace Plataforma.Mobile.Platforms.Android;

[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class ServicioMensajesFirebase : FirebaseMessagingService
{
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (IPlatformApplication.Current?.Services.GetService<NotificacionesDispositivo>() is { } registro)
                await registro.ActualizarAsync();
        });
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);
        var gestor = (NotificationManager)GetSystemService(NotificationService)!;
        const string canal = "reasignaciones";
        Notification.Builder aviso;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            gestor.CreateNotificationChannel(new NotificationChannel(canal, "Asignaciones de ruta", NotificationImportance.High));
            aviso = new Notification.Builder(this, canal);
        }
        else aviso = new Notification.Builder(this);
        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23)) flags |= PendingIntentFlags.Immutable;
        var abrir = PackageManager!.GetLaunchIntentForPackage(PackageName!)!;
        abrir.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pendiente = PendingIntent.GetActivity(this, 4202, abrir, flags);
        var notificacion = aviso.SetContentTitle(message.GetNotification()?.Title ?? "Nueva asignacion")!
            .SetContentText("Tu ruta cambio. Abri la app para actualizarla.")!
            .SetSmallIcon(Resource.Mipmap.appicon)!.SetAutoCancel(true)!.SetContentIntent(pendiente)!.Build();
        gestor.Notify(4202, notificacion);
    }
}