using Microsoft.Extensions.Logging;

namespace Plataforma.Mobile.Services;

public sealed class NotificacionesDispositivo(IRepartidorService repartidor,
    ILogger<NotificacionesDispositivo> logger)
{
    private readonly SemaphoreSlim registro = new(1, 1);
    public string Estado { get; private set; } = "Notificaciones pendientes de registro";
    public async Task ActualizarAsync()
    {
        if (!await registro.WaitAsync(0)) return;
        try
        {
#if ANDROID
            if (!repartidor.HaySesion || !repartidor.ConexionSimulada) return;
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                var gestor = (Android.App.NotificationManager)Android.App.Application.Context
                    .GetSystemService(Android.Content.Context.NotificationService)!;
                gestor.CreateNotificationChannel(new Android.App.NotificationChannel(
                    "reasignaciones", "Asignaciones de ruta", Android.App.NotificationImportance.High));
            }
            if (Firebase.FirebaseApp.InitializeApp(Android.App.Application.Context) is null)
            {
                Estado = "Notificaciones: Firebase sin configurar";
                logger.LogWarning("FCM sin configurar: falta google-services.json para esta aplicacion");
                return;
            }
            var permiso = !OperatingSystem.IsAndroidVersionAtLeast(33) ||
                await Permissions.RequestAsync<RastreoUbicacion.PermisoNotificaciones>() == PermissionStatus.Granted;
            var resultado = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new TokenRecibido(resultado);
            Firebase.Messaging.FirebaseMessaging.Instance.GetToken().AddOnCompleteListener(callback);
            var token = await resultado.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var anterior = await SecureStorage.Default.GetAsync("repartidor.fcm.token");
            await repartidor.RegistrarDispositivoAsync(token, "android");
            if (!string.IsNullOrWhiteSpace(anterior) && anterior != token)
                await repartidor.BorrarDispositivoAsync(anterior);
            await SecureStorage.Default.SetAsync("repartidor.fcm.token", token);
            Estado = permiso ? "Notificaciones registradas" : "Notificaciones: falta permiso del telefono";
#endif
        }
        catch (Exception ex)
        {
            Estado = "Notificaciones pendientes de registro";
            logger.LogWarning(ex, "No se pudo registrar FCM; se reintentara al abrir la ruta");
        }
        finally { registro.Release(); }
    }

    public async Task QuitarAsync()
    {
        await registro.WaitAsync();
        try
        {
            var token = await SecureStorage.Default.GetAsync("repartidor.fcm.token");
            if (!string.IsNullOrWhiteSpace(token) && repartidor.ConexionSimulada)
                await repartidor.BorrarDispositivoAsync(token);
        }
        catch (Exception ex) { logger.LogWarning(ex, "No se pudo quitar el registro push al salir"); }
        finally { registro.Release(); }
    }

#if ANDROID
    private sealed class TokenRecibido(TaskCompletionSource<string> resultado)
        : Java.Lang.Object, Android.Gms.Tasks.IOnCompleteListener
    {
        public void OnComplete(Android.Gms.Tasks.Task task)
        {
            if (task.IsSuccessful && task.Result?.ToString() is { Length: > 0 } token)
                resultado.TrySetResult(token);
            else resultado.TrySetException(new InvalidOperationException("No se obtuvo el token FCM."));
        }
    }
#endif
}
