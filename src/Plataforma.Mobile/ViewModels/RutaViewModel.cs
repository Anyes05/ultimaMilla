using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Plataforma.Mobile.Models;
using Plataforma.Mobile.Services;

namespace Plataforma.Mobile.ViewModels;

public partial class RutaViewModel(
    IRepartidorService repartidor,
    INavigationService navigation,
    ILogger<RutaViewModel> logger) : ObservableObject
{
    public ObservableCollection<Envio> Envios { get; } = new();

    [ObservableProperty]
    private string titulo = "Ruta del dia";

    [ObservableProperty]
    private string resumen = string.Empty;

    [ObservableProperty]
    private string mensaje = string.Empty;

    [ObservableProperty]
    private bool conexionSimulada = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaySeleccion))]
    [NotifyPropertyChangedFor(nameof(MostrarRuta))]
    private Envio? envioSeleccionado;

    [ObservableProperty]
    private string nombreReceptor = string.Empty;

    [ObservableProperty]
    private string observaciones = string.Empty;

    [ObservableProperty]
    private bool ocupado;

    public bool HaySeleccion => EnvioSeleccionado is not null;
    public bool MostrarRuta => !HaySeleccion;

    partial void OnConexionSimuladaChanged(bool value) => repartidor.ConexionSimulada = value;

    [RelayCommand]
    private void Cargar()
    {
        Mensaje = string.Empty;
        CancelarEntrega();
        ConexionSimulada = repartidor.ConexionSimulada;
        Ejecutar(ActualizarRuta);
    }

    [RelayCommand]
    private void SeleccionarEntrega(Envio envio)
    {
        Mensaje = string.Empty;
        if (!envio.PuedeEntregar)
        {
            Mensaje = "Este envio no permite registrar otra entrega.";
            return;
        }
        EnvioSeleccionado = envio;
        NombreReceptor = string.Empty;
        Observaciones = string.Empty;
    }

    [RelayCommand]
    private void CancelarEntrega()
    {
        EnvioSeleccionado = null;
        NombreReceptor = string.Empty;
        Observaciones = string.Empty;
    }

    [RelayCommand]
    private void GuardarEntrega()
    {
        if (EnvioSeleccionado is null)
        {
            Mensaje = "Selecciona un envio.";
            return;
        }

        Ejecutar(() =>
        {
            repartidor.RegistrarEntrega(EnvioSeleccionado.Id, NombreReceptor, Observaciones);
            CancelarEntrega();
            ActualizarRuta();
            Mensaje = "Entrega pendiente guardada en SQLite. Se conserva al cerrar la app.";
        });
    }

    [RelayCommand]
    private void Sincronizar()
    {
        Ejecutar(() =>
        {
            var resultado = repartidor.Sincronizar();
            ActualizarRuta();
            Mensaje = $"Simulacion: {resultado.Aplicadas} aceptadas, {resultado.Rechazadas} rechazadas; " +
                      $"{resultado.Pendientes} sin resolver. Los rechazos conservan el comprobante local.";
        });
    }

    [RelayCommand]
    private async Task CerrarSesionAsync()
    {
        if (Ocupado)
            return;
        Ocupado = true;
        try
        {
            repartidor.CerrarSesion();
            Envios.Clear();
            CancelarEntrega();
            await navigation.MostrarLoginAsync();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo cerrar la sesion");
            Mensaje = ex.Message;
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void ActualizarRuta()
    {
        var ruta = repartidor.ObtenerRuta();
        Titulo = $"Ruta {ruta.Fecha:dd/MM/yyyy} - {ruta.Operador}";
        Envios.Clear();
        foreach (var envio in ruta.Envios)
            Envios.Add(envio);

        Resumen = $"{Envios.Count} envios | " +
                  $"{Envios.Count(item => item.Sincronizacion == EstadoSincronizacion.Pendiente)} pendientes | " +
                  $"{Envios.Count(item => item.Sincronizacion == EstadoSincronizacion.Rechazado)} rechazados";
    }

    private void Ejecutar(Action accion)
    {
        Mensaje = string.Empty;
        try
        {
            accion();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo completar la operacion de ruta");
            Mensaje = ex.Message;
        }
    }
}
