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
    public bool PantallaActiva { get; set; }
    public string? Foto { get; set; }
    public string? Firma { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    [ObservableProperty]
    private bool intentoFallido;
    [ObservableProperty]
    private string tokenDispositivo = string.Empty;
    public bool MostrarRuta => !HaySeleccion;
    public string ModoTexto => "API: servidor configurado";

    partial void OnConexionSimuladaChanged(bool value) => repartidor.ConexionSimulada = value;

    [RelayCommand]
    private void Cargar()
    {
        if (Ocupado) return;
        OnPropertyChanged(nameof(ModoTexto));
        Mensaje = string.Empty;
        CancelarEntrega();
        ConexionSimulada = repartidor.ConexionSimulada;
        Ejecutar(ActualizarRuta);
    }

    [RelayCommand]
    private void SeleccionarEntrega(Envio envio)
    {
        if (Ocupado) return;
        Mensaje = string.Empty;
        if (!envio.PuedeEntregar)
        {
            Mensaje = "Este envio no permite registrar otra entrega.";
            return;
        }
        EnvioSeleccionado = envio;
        NombreReceptor = string.Empty;
        Observaciones = string.Empty;
        Foto = Firma = null;
        Latitud = Longitud = null;
        IntentoFallido = false;
    }

    [RelayCommand]
    private void CancelarEntrega()
    {
        if (Ocupado) return;
        EnvioSeleccionado = null;
        NombreReceptor = string.Empty;
        Observaciones = string.Empty;
    }

    [RelayCommand]
    private void GuardarEntrega()
    {
        if (Ocupado) return;
        if (EnvioSeleccionado is null)
        {
            Mensaje = "Selecciona un envio.";
            return;
        }

        Ejecutar(() =>
        {
            if (Foto is null && Firma is null && Latitud is null && !IntentoFallido)
                repartidor.RegistrarEntrega(EnvioSeleccionado.Id, NombreReceptor, Observaciones);
            else
                repartidor.RegistrarEvento(new EntregaPendiente(Guid.NewGuid(), EnvioSeleccionado.Id,
                    NombreReceptor.Trim(), Observaciones.Trim(), DateTimeOffset.Now,
                    IntentoFallido ? EstadoEnvio.NoEntregado : EstadoEnvio.Entregado,
                    Foto, Firma, Latitud, Longitud));
            CancelarEntrega();
            ActualizarRuta();
            Mensaje = "Entrega pendiente guardada en SQLite. Se conserva al cerrar la app.";
        });
    }

    public void RegistrarCarga(Envio envio, string codigo)
    {
        if (Ocupado) return;
        Ejecutar(() =>
        {
            repartidor.RegistrarEvento(new EntregaPendiente(Guid.NewGuid(), envio.Id, "", "",
                DateTimeOffset.Now, EstadoEnvio.EnTransito, CodigoEscaneado: codigo));
            ActualizarRuta();
            Mensaje = "Carga guardada. Sincroniza antes de entregar: se requiere confirmacion del servidor.";
        });
    }

    public async Task AutoSincronizarAsync()
    {
        try
        {
            if (!PantallaActiva || Ocupado || HaySeleccion || !repartidor.HaySesion || !ConexionSimulada) return;
            await SincronizarAsync();
        }
        catch (InvalidOperationException ex) { Mensaje = ex.Message; }
    }

    [RelayCommand]
    private Task AltaDispositivoAsync() => DispositivoAsync(false);

    [RelayCommand]
    private Task BajaDispositivoAsync() => DispositivoAsync(true);

    private async Task DispositivoAsync(bool borrar)
    {
        if (Ocupado) return;
        Ocupado = true;
        try
        {
            if (borrar) await repartidor.BorrarDispositivoAsync(TokenDispositivo);
            else await repartidor.RegistrarDispositivoAsync(TokenDispositivo, "android");
            Mensaje = borrar ? "Token eliminado." : "Token registrado. No implica recepcion FCM.";
        }
        catch (InvalidOperationException ex) { Mensaje = ex.Message; }
        finally { Ocupado = false; }
    }

    [RelayCommand]
    private async Task SincronizarAsync()
    {
        if (Ocupado) return;
        Ocupado = true;
        Mensaje = string.Empty;
        try
        {
            var resultado = await repartidor.SincronizarAsync();
            ActualizarRuta();
            Mensaje = $"API: {resultado.Aplicadas} aceptadas, {resultado.Rechazadas} rechazadas; " +
                      $"{resultado.Pendientes} sin resolver. Los rechazos conservan el comprobante local.";
        }

        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo sincronizar la cola");
            Mensaje = ex.Message;
        }
        finally { Ocupado = false; }
    }

    [RelayCommand]
    private async Task ReintentarRechazadosAsync()
    {
        if (Ocupado) return;
        try { repartidor.ReintentarRechazados(); }
        catch (InvalidOperationException ex) { Mensaje = ex.Message; return; }
        await SincronizarAsync();
    }

    [RelayCommand]
    private async Task DescargarRutaAsync()
    {
        if (Ocupado) return;
        Ocupado = true;
        Mensaje = string.Empty;
        try
        {
            await repartidor.DescargarRutaAsync();
            ActualizarRuta();
            Mensaje = "Ruta actualizada.";
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo descargar la ruta");
            Mensaje = ex.Message;
        }
        finally { Ocupado = false; }
    }

    [RelayCommand]
    private async Task VolverAccesoAsync()
    {
        if (!Ocupado) await navigation.MostrarLoginAsync();
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
