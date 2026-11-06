using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Plataforma.Mobile.Services;

namespace Plataforma.Mobile.ViewModels;

public partial class LoginViewModel(
    IRepartidorService repartidor,
    INavigationService navigation,
    ILogger<LoginViewModel> logger) : ObservableObject
{
    [ObservableProperty]
    private string usuario = "alvaro@demo.test";

    [ObservableProperty]
    private string clave = string.Empty;

    [ObservableProperty]
    private string mensaje = string.Empty;

    [ObservableProperty]
    private bool ocupado;

    public string ModoTexto => "Acceso a tu jornada de reparto";

    public void PrepararAcceso()
    {
        try
        {
            if (repartidor.RequiereAutenticacion)
                Mensaje = "Tu sesion ya no es valida. Ingresa con la misma cuenta para enviar tus comprobantes pendientes. No se borraron tus datos.";
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo consultar la sesion guardada");
            Mensaje = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ContinuarSesionAsync()
    {
        if (Ocupado)
            return;
        Ocupado = true;
        Mensaje = string.Empty;
        try
        {
            if (!repartidor.HaySesion)
            {
                Mensaje = "No hay una sesion guardada. Inicia sesion primero.";
                return;
            }
            await navigation.MostrarRutaAsync();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo recuperar la sesion local");
            Mensaje = ex.Message;
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private async Task IngresarAsync()
    {
        if (Ocupado)
            return;

        Mensaje = string.Empty;
        if (string.IsNullOrWhiteSpace(Usuario) || string.IsNullOrWhiteSpace(Clave))
        {
            Mensaje = "Completa el usuario y la clave.";
            return;
        }

        Ocupado = true;
        try
        {
            if (!await repartidor.IniciarSesionAsync(Usuario, Clave))
            {
                Mensaje = "No pudimos ingresar con esos datos. Revisa tu usuario y clave e intentalo de nuevo.";
                return;
            }

            Clave = string.Empty;
            await navigation.MostrarRutaAsync();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo iniciar la sesion");
            Mensaje = ex.Message;
        }
        finally
        {
            Ocupado = false;
        }
    }
}
