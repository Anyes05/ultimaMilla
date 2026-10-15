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
                Mensaje = "No hay una sesion demo guardada. Ingresa primero con las credenciales de prueba.";
                return;
            }
            await navigation.MostrarRutaAsync();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo recuperar la sesion demo local");
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
            Mensaje = "Completa el usuario y la clave de demostracion.";
            return;
        }

        Ocupado = true;
        try
        {
            if (!repartidor.IniciarSesion(Usuario, Clave))
            {
                Mensaje = "Usuario o clave incorrectos. Usa las credenciales de demostracion.";
                return;
            }

            Clave = string.Empty;
            await navigation.MostrarRutaAsync();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo iniciar la sesion de demostracion");
            Mensaje = ex.Message;
        }
        finally
        {
            Ocupado = false;
        }
    }
}
