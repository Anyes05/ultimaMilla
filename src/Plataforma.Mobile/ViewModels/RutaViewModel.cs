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
    public ObservableCollection<Envio> EnviosVisibles { get; } = new();
    private Guid[] recorrido = [];
    private int paradaActual;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarListaDiaria))]
    private bool recorridoActivo;

    [ObservableProperty]
    private string progresoRecorrido = string.Empty;

    public bool MostrarListaDiaria => !RecorridoActivo;

    [RelayCommand]
    private void EmpezarRecorrido()
    {
        if (Ocupado || HaySeleccion) return;
        recorrido = Envios.Where(e => e.PuedeEntregar || e.PuedeCargar).Select(e => e.Id).ToArray();
        if (recorrido.Length == 0)
        {
            Mensaje = "No hay nuevas entregas disponibles. Revisa los pendientes de sincronizar.";
            return;
        }
        paradaActual = 0;
        RecorridoActivo = true;
        Mensaje = string.Empty;
        ActualizarVisibles();
    }

    [RelayCommand]
    private void PausarRecorrido()
    {
        if (Ocupado || HaySeleccion) return;
        RecorridoActivo = false;
        ActualizarVisibles();
        Mensaje = "Recorrido pausado. Tus registros siguen guardados.";
    }

    [RelayCommand]
    private void ParadaAnterior()
    {
        if (Ocupado || HaySeleccion || !RecorridoActivo) return;
        if (paradaActual > 0) paradaActual--;
        ActualizarVisibles();
    }

    private void ActualizarVisibles()
    {
        EnviosVisibles.Clear();
        if (!RecorridoActivo)
        {
            foreach (var envio in Envios) EnviosVisibles.Add(envio);
            return;
        }
        var actual = Envios.SingleOrDefault(e => e.Id == recorrido[paradaActual]);
        if (actual is not null) EnviosVisibles.Add(actual);
        ProgresoRecorrido = $"Parada {paradaActual + 1} de {recorrido.Length}";
    }

    private void AvanzarRecorrido()
    {
        if (!RecorridoActivo) return;
        var siguiente = Enumerable.Range(paradaActual + 1, recorrido.Length - paradaActual - 1)
            .FirstOrDefault(i => Envios.Any(e => e.Id == recorrido[i] && (e.PuedeEntregar || e.PuedeCargar)), -1);
        if (siguiente >= 0)
        {
            paradaActual = siguiente;
            ActualizarVisibles();
            return;
        }
        RecorridoActivo = false;
        ActualizarVisibles();
        Mensaje = "Recorrido completado. Revisa y sincroniza los comprobantes pendientes.";
    }

    [RelayCommand]
    private void SiguienteParada()
    {
        if (Ocupado || HaySeleccion || !RecorridoActivo) return;
        if (EnviosVisibles.Any(e => e.PuedeEntregar || e.PuedeCargar))
        {
            Mensaje = "Registra la entrega o el intento antes de continuar. Puedes pausar para volver a la lista.";
            return;
        }
        AvanzarRecorrido();
    }

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

    [ObservableProperty]
    private bool mostrarOpciones;

    [RelayCommand]
    private void AlternarOpciones() => MostrarOpciones = !MostrarOpciones;

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
    public string ModoTexto => "Tu ruta de reparto";

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
            Mensaje = "Comprobante guardado. Pendiente de sincronizar.";
            AvanzarRecorrido();
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
            Mensaje = "Carga guardada. Sincroniza para habilitar la entrega.";
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
            Mensaje = $"{resultado.Aplicadas} registros sincronizados, {resultado.Rechazadas} rechazados; " +
                      $"{resultado.Pendientes} por resolver. Tus comprobantes se conservan.";
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
        if (RecorridoActivo)
        {
            Mensaje = "Pausa el recorrido antes de actualizar la ruta.";
            return;
        }
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
    private async Task CerrarSesionAsync()
    {
        if (Ocupado)
            return;
        Ocupado = true;
        try
        {
            repartidor.CerrarSesion();
            RecorridoActivo = false;
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
                  $"{Envios.Count(item => item.Estado == EstadoEnvio.Entregado)} entregados | " +
                  $"{Envios.Count(item => item.Sincronizacion == EstadoSincronizacion.Pendiente)} pendientes | " +
                  $"{Envios.Count(item => item.Sincronizacion == EstadoSincronizacion.Rechazado)} rechazados";
        ActualizarVisibles();
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
