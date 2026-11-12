namespace Plataforma.Mobile.Services;

public sealed class AutenticacionBiometrica
{
    public async Task<bool> ConfirmarAsync(string operacion)
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(28) && Platform.CurrentActivity is { } actividad)
        {
            var resultado = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancelacion = new Android.OS.CancellationSignal();
            using var callback = new ResultadoAutenticacion(resultado);
            using var cancelar = new CancelarAutenticacion(resultado);
            using var prompt = new Android.Hardware.Biometrics.BiometricPrompt.Builder(actividad)
                .SetTitle("Verificar identidad")!
                .SetDescription(operacion)!
                .SetNegativeButton("Cancelar", actividad.MainExecutor!, cancelar)!
                .Build();
            prompt.Authenticate(cancelacion, actividad.MainExecutor!, callback);
            return await resultado.Task;
        }
#endif
        await Shell.Current.DisplayAlertAsync("Biometria no disponible",
            "Configura huella o reconocimiento facial en un dispositivo compatible para esta operacion.", "Aceptar");
        return false;
    }

#if ANDROID
    private sealed class ResultadoAutenticacion(TaskCompletionSource<bool> resultado)
        : Android.Hardware.Biometrics.BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(Android.Hardware.Biometrics.BiometricPrompt.AuthenticationResult? result)
            => resultado.TrySetResult(true);

        public override void OnAuthenticationError(Android.Hardware.Biometrics.BiometricErrorCode errorCode,
            Java.Lang.ICharSequence? errString) => resultado.TrySetResult(false);
    }

    private sealed class CancelarAutenticacion(TaskCompletionSource<bool> resultado)
        : Java.Lang.Object, Android.Content.IDialogInterfaceOnClickListener
    {
        public void OnClick(Android.Content.IDialogInterface? dialog, int which) => resultado.TrySetResult(false);
    }
#endif
}