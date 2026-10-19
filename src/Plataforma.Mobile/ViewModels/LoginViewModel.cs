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

    public bool ModoDemo
    {
        get => repartidor.ModoDemo;
        set
        {
            if (Ocupado || value == repartidor.ModoDemo) return;
            repartidor.ModoDemo = value;
            Usuario = value ? "alvaro@demo.test" : string.Empty;
            Clave = string.Empty;
            Mensaje = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModoTexto));
        }
    }

    public string ModoTexto => ModoDemo
        ? "DEMO EXPLICITA: servidor simulado; datos separados de la API."
        : "API REAL: https://api.tallernet.cloud-ip.cc";

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
                Mensaje = "No hay una sesion guardada en este modo. Inicia sesion primero.";
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
            Mensaje = "Completa el usuario y la clave.";
            return;
        }

        Ocupado = true;
        try
        {
            if (!await repartidor.IniciarSesionAsync(Usuario, Clave))
            {
                Mensaje = "Usuario o clave incorrectos para el modo seleccionado.";
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
