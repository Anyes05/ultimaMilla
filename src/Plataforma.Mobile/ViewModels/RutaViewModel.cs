using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Plataforma.Mobile.Models;
using Plataforma.Mobile.Services;
using EstadoBulto = Plataforma.Domain.EstadoBulto;
using PoliticaBultosPendientes = Plataforma.Domain.PoliticaBultosPendientes;
using DerivacionEstadoEnvio = Plataforma.Domain.DerivacionEstadoEnvio;

namespace Plataforma.Mobile.ViewModels;

public partial class RutaViewModel(
    IRepartidorService repartidor,
    INavigationService navigation,
    ILogger<RutaViewModel> logger) : ObservableObject
{
    public ObservableCollection<Envio> Envios { get; } = new();
    public ObservableCollection<Envio> EnviosVisibles { get; } = new();
    public ObservableCollection<EntregaPendiente> EntregasHistorial { get; } = new();
    public ObservableCollection<BultoEntregaItem> BultosEntrega { get; } = new();
    public ObservableCollection<RetiroEnRuta> Retiros { get; } = new();
    public ObservableCollection<string> CodigosRetiro { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaySeleccion))]
    [NotifyPropertyChangedFor(nameof(MostrarRuta))]
    [NotifyPropertyChangedFor(nameof(EsRetiro))]
    [NotifyPropertyChangedFor(nameof(UsaEntregaParcial))]
    [NotifyPropertyChangedFor(nameof(UsaEntregaCompleta))]
    [NotifyPropertyChangedFor(nameof(ReferenciaSeleccionada))]
    [NotifyPropertyChangedFor(nameof(TituloComprobante))]
    private RetiroEnRuta? retiroSeleccionado;

    public bool EsRetiro => RetiroSeleccionado is not null;
    public string TituloComprobante => EsRetiro ? "Registrar retiro" : "Registrar entrega";
    public string ReferenciaSeleccionada => RetiroSeleccionado?.Direccion ?? EnvioSeleccionado?.NumeroSeguimiento ?? string.Empty;
    public string ResumenCodigosRetiro => $"{CodigosRetiro.Count} de {RetiroSeleccionado?.CantidadBultos ?? 0} bultos: {string.Join(", ", CodigosRetiro)}";

    [RelayCommand]
    private void SeleccionarRetiro(RetiroEnRuta retiro)
    {
        if (Ocupado || !retiro.PuedeRetirar) return;
        CancelarEntrega();
        RetiroSeleccionado = retiro;
        Foto = Firma = null;
        Latitud = Longitud = null;
        CodigosRetiro.Clear();
        OnPropertyChanged(nameof(ResumenCodigosRetiro));
    }

    public void RegistrarCodigoRetiro(string codigo)
    {
        if (RetiroSeleccionado is null) return;
        if (codigo.Length > 120 || CodigosRetiro.Contains(codigo) || CodigosRetiro.Count >= RetiroSeleccionado.CantidadBultos)
        { Mensaje = "Bulto repetido, sobrante o codigo invalido. No se agrego al retiro."; return; }
        CodigosRetiro.Add(codigo);
        OnPropertyChanged(nameof(ResumenCodigosRetiro));
    }
    private Guid[] recorrido = [];
    private int paradaActual;
    private Ruta? rutaActual;
    public bool PuedeIniciarRuta => rutaActual is { Id: var id, IniciadaEn: null } && id != Guid.Empty &&
        (Envios.Any(envio => envio.Estado is EstadoEnvio.AsignadoARuta or EstadoEnvio.EnTransito) ||
         Retiros.Any(retiro => retiro.Estado == Plataforma.Domain.EstadoRetiro.Programado));
    public string EstadoInicioRuta => rutaActual?.IniciadaEn is { } inicio
        ? $"Ruta iniciada: {inicio.ToLocalTime():dd/MM HH:mm}"
        : "Ruta pendiente de inicio";

    [RelayCommand]
    private async Task IniciarRutaAsync()
    {
        if (Ocupado || !PuedeIniciarRuta) return;
        Ocupado = true;
        try
        {
            await repartidor.IniciarRutaAsync();
            await repartidor.DescargarRutaAsync();
            ActualizarRuta();
            Mensaje = "Ruta iniciada. Puedes comenzar el reparto.";
        }
        catch (SesionRepartidorVencidaException ex) { await PedirAutenticacionAsync(ex); }
        catch (InvalidOperationException ex) { Mensaje = ex.Message; }
        finally { Ocupado = false; }
    }
    private bool entregaParcialHabilitada;
    private IReadOnlyList<EntregaPendiente> historialCompleto = [];
    private IReadOnlyList<EntregaPendiente> historialFiltrado = [];
    private const int TamanoPaginaHistorial = 10;

    [ObservableProperty]
    private bool recorridoActivo;

    [ObservableProperty]
    private string progresoRecorrido = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(UsaEntregaParcial))]
    [NotifyPropertyChangedFor(nameof(UsaEntregaCompleta))]
    [NotifyPropertyChangedFor(nameof(ReferenciaSeleccionada))]
    private Envio? envioSeleccionado;

    [ObservableProperty]
    private string nombreReceptor = string.Empty;

    [ObservableProperty]
    private string documentoReceptor = string.Empty;

    [ObservableProperty]
    private string observaciones = string.Empty;

    [ObservableProperty]
    private bool ocupado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarRuta))]
    private bool mostrarHistorial;

    [ObservableProperty]
    private bool menuAbierto;

    [ObservableProperty]
    private DateTime fechaHistorial = DateTime.Today;

    [ObservableProperty]
    private string textoFechaHistorial = string.Empty;

    [ObservableProperty]
    private string mensajeFiltroHistorial = string.Empty;

    [ObservableProperty]
    private string resumenHistorial = string.Empty;

    partial void OnFechaHistorialChanged(DateTime value) => ActualizarHistorialVisible();

    partial void OnTextoFechaHistorialChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            FiltrarHistorialPorFecha = false;
            MensajeFiltroHistorial = string.Empty;
            ActualizarHistorialVisible();
            return;
        }

        if (DateOnly.TryParseExact(value.Trim(), ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            FechaHistorial = fecha.ToDateTime(TimeOnly.MinValue);
            FiltrarHistorialPorFecha = true;
            MensajeFiltroHistorial = string.Empty;
            ActualizarHistorialVisible();
            return;
        }

        FiltrarHistorialPorFecha = false;
        MensajeFiltroHistorial = "Usa el formato dd/MM/aaaa.";
        ActualizarHistorialVisible();
    }

    public bool FiltrarHistorialPorFecha { get; private set; }

    [RelayCommand]
    private void AlternarMenu() => MenuAbierto = !MenuAbierto;

    [RelayCommand]
    private void AbrirHistorial()
    {
        if (Ocupado || HaySeleccion) return;
        MenuAbierto = false;
        TextoFechaHistorial = string.Empty;
        historialCompleto = repartidor.ObtenerHistorialEntregas()
            .OrderByDescending(comprobante => comprobante.Fecha)
            .ToArray();
        MostrarHistorial = true;
        ActualizarHistorialVisible();
    }

    [RelayCommand]
    private async Task VolverARutaAsync()
    {
        MostrarHistorial = false;
        await RefrescarRutaAlMostrarAsync();
    }

    [RelayCommand]
    private void LimpiarFiltroHistorial() => TextoFechaHistorial = string.Empty;

    [RelayCommand]
    private void CargarMasHistorial()
    {
        var visibles = EntregasHistorial.Count;
        foreach (var comprobante in historialFiltrado.Skip(visibles).Take(TamanoPaginaHistorial))
            EntregasHistorial.Add(comprobante);
        ActualizarResumenHistorial();
    }

    private void ActualizarHistorialVisible()
    {
        historialFiltrado = FiltrarHistorialPorFecha
            ? historialCompleto.Where(comprobante => comprobante.Fecha.LocalDateTime.Date == FechaHistorial.Date).ToArray()
            : historialCompleto;
        EntregasHistorial.Clear();
        foreach (var comprobante in historialFiltrado.Take(TamanoPaginaHistorial))
            EntregasHistorial.Add(comprobante);
        ActualizarResumenHistorial();
    }

    private void ActualizarResumenHistorial()
    {
        var etiquetaEntregas = historialFiltrado.Count == 1 ? "entrega" : "entregas";
        ResumenHistorial = $"Mostrando {EntregasHistorial.Count} de {historialFiltrado.Count} {etiquetaEntregas}";
    }

    public bool HaySeleccion => EnvioSeleccionado is not null || EsRetiro;
    public bool TieneComprobantesSinResolver => repartidor.ObtenerCantidadSincronizacionesPendientes() > 0 ||
        Envios.Any(envio => envio.Sincronizacion == EstadoSincronizacion.Rechazado) ||
        Envios.Any(envio => envio.BultosEscaneadosPendientes?.Count > 0) ||
        repartidor.ObtenerCantidadComprobantesFueraDeRuta() > 0 ||
        Retiros.Any(retiro => retiro.Sincronizacion == EstadoSincronizacion.Rechazado);
    public bool SinEnvios => Envios.Count == 0 && Retiros.Count == 0;
    public bool UsaEntregaParcial =>
        EnvioSeleccionado?.Bultos?.Any(bulto => bulto.PuedeEntregarse) ?? false;
    public bool UsaEntregaCompleta => !EsRetiro && !UsaEntregaParcial;
    public string EvidenciaRequeridaTexto => rutaActual is null
        ? string.Empty
        : EsRetiro ? "Retiro: foto, firma, responsable y todos los bultos obligatorios"
        : $"Firma: {(rutaActual.ExigeFirma ? "obligatoria" : "opcional")} | Foto: {(rutaActual.ExigeFoto ? "obligatoria" : "opcional")} | Documento: {(rutaActual.ExigeDocumentoReceptor ? "obligatorio" : "opcional")}";
    public string ResumenBultosEntrega
    {
        get
        {
            var total = EnvioSeleccionado?.Bultos?.Count ?? BultosEntrega.Count;
            var entregadosAntes = EnvioSeleccionado?.Bultos?.Count(item => item.Estado == EstadoBulto.Entregado) ?? 0;
            return $"{entregadosAntes + BultosEntrega.Count(item => item.Entregado)} de {total} bultos entregados";
        }
    }
    public bool PantallaActiva { get; set; }
    public bool SoloRutasDeHoy { get; set; }
    public string? Foto { get; set; }
    public string? Firma { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    [ObservableProperty]
    private bool intentoFallido;
    [ObservableProperty]
    private string tokenDispositivo = string.Empty;
    [ObservableProperty]
    private string estadoPush = "Notificaciones pendientes de registro";
    public bool MostrarRuta => !HaySeleccion && !MostrarHistorial;
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

    public async Task RefrescarRutaAlMostrarAsync()
    {
        if (Ocupado || RecorridoActivo || HaySeleccion || !repartidor.HaySesion) return;
        ConexionSimulada = repartidor.ConexionSimulada;
        try { ActualizarRuta(); }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo mostrar la ruta local antes de actualizarla");
            Mensaje = ex.Message;
            return;
        }
        await DescargarRutaAsync();
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
        DocumentoReceptor = string.Empty;
        Observaciones = string.Empty;
        Foto = Firma = null;
        Latitud = Longitud = null;
        IntentoFallido = false;
        BultosEntrega.Clear();
        var bultosEnvio = envio.Bultos ?? [];
        if (bultosEnvio.Count > 0)
        {
            var motivos = rutaActual?.MotivosNoEntrega ?? CatalogoMotivosNoEntrega.Predeterminados;
            foreach (var bulto in bultosEnvio.Where(bulto => bulto.PuedeEntregarse))
                BultosEntrega.Add(new BultoEntregaItem(
                    bulto, motivos, envio.BultosEscaneadosPendientes?.Contains(bulto.Id) == true));
        }
        OnPropertyChanged(nameof(ResumenBultosEntrega));
    }

    [RelayCommand]
    private void CancelarEntrega()
    {
        if (Ocupado) return;
        EnvioSeleccionado = null;
        RetiroSeleccionado = null;
        DocumentoReceptor = string.Empty;
        CodigosRetiro.Clear();
        NombreReceptor = string.Empty;
        Observaciones = string.Empty;
        BultosEntrega.Clear();
        OnPropertyChanged(nameof(ResumenBultosEntrega));
    }

    [RelayCommand]
    private async Task GuardarEntregaAsync()
    {
        if (Ocupado) return;
        if (RetiroSeleccionado is { } retiro)
        {
            var guardadoRetiro = false;
            Ejecutar(() =>
            {
                if (CodigosRetiro.Count != retiro.CantidadBultos || string.IsNullOrWhiteSpace(NombreReceptor) ||
                    string.IsNullOrWhiteSpace(Foto) || string.IsNullOrWhiteSpace(Firma) || Latitud is null || Longitud is null)
                    throw new InvalidOperationException("El retiro requiere todos los bultos, responsable, foto, firma y GPS.");
                repartidor.RegistrarRetiro(new RetiroPendiente(Guid.NewGuid(), retiro.Id, retiro.RutaId,
                    DateTimeOffset.Now, CodigosRetiro.ToArray(), NombreReceptor.Trim(), Foto, Firma, Latitud.Value, Longitud.Value));
                CancelarEntrega();
                ActualizarRuta();
                Mensaje = "Retiro guardado en el telefono. La evidencia se conserva hasta su confirmacion.";
                guardadoRetiro = true;
            });
            if (guardadoRetiro && ConexionSimulada) await SincronizarColaAsync(automatica: true);
            return;
        }
        if (EnvioSeleccionado is null)
        {
            Mensaje = "Selecciona un envio.";
            return;
        }

        var guardado = false;
        Ejecutar(() =>
        {
            if (rutaActual?.ExigeDocumentoReceptor == true && string.IsNullOrWhiteSpace(DocumentoReceptor) &&
                (UsaEntregaParcial ? BultosEntrega.Any(item => item.Entregado) : !IntentoFallido))
                throw new InvalidOperationException("La regla del operador exige el documento del receptor.");
            if (UsaEntregaParcial)
            {
                if (!entregaParcialHabilitada && BultosEntrega.Any(item => item.Entregado) &&
                    BultosEntrega.Any(item => !item.Entregado))
                    throw new InvalidOperationException("Escanea todos los bultos para entregar el envio. El operador no permite entregas parciales.");
                if (BultosEntrega.Any(item => !item.Entregado && string.IsNullOrWhiteSpace(item.MotivoNoEntrega)))
                    throw new InvalidOperationException("Selecciona un motivo del catálogo para cada bulto no entregado.");
                if (BultosEntrega.Any(item => item.Entregado) && string.IsNullOrWhiteSpace(NombreReceptor))
                    throw new InvalidOperationException("Ingresa el nombre de quien recibe los bultos entregados.");
                if (BultosEntrega.Any(item => item.Entregado) && rutaActual?.ExigeFirma == true && string.IsNullOrWhiteSpace(Firma))
                    throw new InvalidOperationException("La regla del operador exige registrar la firma antes de guardar.");
                if (BultosEntrega.Any(item => item.Entregado) && rutaActual?.ExigeFoto == true && string.IsNullOrWhiteSpace(Foto))
                    throw new InvalidOperationException("La regla del operador exige una foto antes de guardar.");

                var bultos = BultosEntrega.Select(item => new ResultadoBultoEntrega(
                    item.Bulto.Id,
                    item.Entregado ? EstadoBulto.Entregado : EstadoBulto.NoEntregado,
                    item.Entregado ? null : item.MotivoNoEntrega,
                    item.Entregado ? Foto : null,
                    item.Entregado ? Firma : null)).ToArray();
                var bultosEnvio = EnvioSeleccionado!.Bultos ?? [];
                var entregadosAntes = bultosEnvio.Count(item => item.Estado == EstadoBulto.Entregado);
                var totalEntregados = entregadosAntes + bultos.Count(item => item.Estado == EstadoBulto.Entregado);
                var estadosFinales = bultosEnvio.Select(bulto => bulto.Estado).ToArray();
                for (var index = 0; index < bultosEnvio.Count; index++)
                {
                    var resultado = bultos.SingleOrDefault(item => item.BultoId == bultosEnvio[index].Id);
                    if (resultado is null) continue;
                    var estadoBulto = resultado.Estado;
                    if (estadoBulto == EstadoBulto.NoEntregado && rutaActual?.AdmiteEntregaParcial == true)
                        estadoBulto = rutaActual.PoliticaBultosPendientes == PoliticaBultosPendientes.DevolucionAlComercio
                            ? EstadoBulto.Devuelto
                            : EstadoBulto.PendienteNuevaVisita;
                    estadosFinales[index] = estadoBulto;
                }
                var estadoDerivado = DerivacionEstadoEnvio.DesdeBultos(estadosFinales);
                var estadoEnvio = Enum.Parse<Plataforma.Mobile.Models.EstadoEnvio>(estadoDerivado.ToString());
                repartidor.RegistrarEvento(new EntregaPendiente(Guid.NewGuid(), EnvioSeleccionado.Id,
                    NombreReceptor.Trim(), Observaciones.Trim(), DateTimeOffset.Now, estadoEnvio,
                    Foto, Firma, Latitud, Longitud, Bultos: bultos, DocumentoReceptor: DocumentoReceptor.Trim()));
            }
            else if (Foto is null && Firma is null && Latitud is null && !IntentoFallido && string.IsNullOrWhiteSpace(DocumentoReceptor))
                repartidor.RegistrarEntrega(EnvioSeleccionado.Id, NombreReceptor, Observaciones);
            else
                repartidor.RegistrarEvento(new EntregaPendiente(Guid.NewGuid(), EnvioSeleccionado.Id,
                    NombreReceptor.Trim(), Observaciones.Trim(), DateTimeOffset.Now,
                    IntentoFallido ? EstadoEnvio.NoEntregado : EstadoEnvio.Entregado,
                    Foto, Firma, Latitud, Longitud, DocumentoReceptor: DocumentoReceptor.Trim()));
            CancelarEntrega();
            ActualizarRuta();
            Mensaje = ConexionSimulada
                ? "Comprobante guardado. Sincronizando ahora..."
                : "Comprobante guardado en el telefono. Se sincronizara al recuperar conexion.";
            AvanzarRecorrido();
            guardado = true;
        });
        if (guardado && ConexionSimulada)
            await SincronizarColaAsync(automatica: true);
    }

    public void RegistrarBultoEntregado(Guid bultoId, string codigoEscaneado)
    {
        if (Ocupado)
        {
            Mensaje = "Hay una operacion en curso. Vuelve a escanear el bulto cuando termine.";
            return;
        }
        if (!UsaEntregaParcial)
        {
            Mensaje = "La entrega seleccionada ya no permite registrar bultos. Vuelve a abrirla.";
            return;
        }
        var item = BultosEntrega.SingleOrDefault(bulto => bulto.Bulto.Id == bultoId);
        if (item is null)
        {
            Mensaje = "El bulto ya no pertenece a la entrega seleccionada. Vuelve a abrirla.";
            return;
        }
        if (!string.Equals(item.Bulto.Codigo, codigoEscaneado, StringComparison.Ordinal))
        {
            Mensaje = "El codigo escaneado no corresponde a este bulto. No se marco como entregado.";
            return;
        }
        repartidor.RegistrarEscaneoBulto(EnvioSeleccionado!.Id, bultoId, codigoEscaneado);
        item.Entregado = true;
        item.MotivoNoEntrega = null;
        OnPropertyChanged(nameof(ResumenBultosEntrega));
        Mensaje = $"Bulto {item.Bulto.Codigo} marcado como entregado.";
    }

    public async Task RegistrarCargaAsync(Envio envio, string codigo)
    {
        if (Ocupado) return;
        var guardada = false;
        Ejecutar(() =>
        {
            repartidor.RegistrarEvento(new EntregaPendiente(Guid.NewGuid(), envio.Id, "", "",
                DateTimeOffset.Now, EstadoEnvio.EnTransito, CodigoEscaneado: codigo));
            ActualizarRuta();
            Mensaje = ConexionSimulada
                ? "Carga guardada. Confirmandola con el servidor..."
                : "Carga guardada en el telefono. Se sincronizara al recuperar conexion.";
            guardada = true;
        });
        if (guardada && ConexionSimulada)
            await SincronizarColaAsync(automatica: true);
    }

    public async Task AutoSincronizarAsync()
    {
        try
        {
            if (!PantallaActiva || Ocupado || HaySeleccion || !repartidor.HaySesion || !ConexionSimulada) return;
            if (repartidor.ObtenerCantidadSincronizacionesPendientes() == 0) return;
            await SincronizarColaAsync(automatica: true);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo iniciar la sincronizacion automatica");
            Mensaje = ex.Message;
        }
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
        catch (SesionRepartidorVencidaException ex) { await PedirAutenticacionAsync(ex); }
        catch (InvalidOperationException ex) { Mensaje = ex.Message; }
        finally { Ocupado = false; }
    }

    [RelayCommand]
    private Task SincronizarAsync() => SincronizarColaAsync(automatica: false);

    private async Task SincronizarColaAsync(bool automatica)
    {
        MenuAbierto = false;
        if (Ocupado)
        {
            if (!automatica) Mensaje = "Hay una operacion en curso. Espera a que termine antes de sincronizar.";
            return;
        }
        Ocupado = true;
        var mensajeAnterior = Mensaje;
        Mensaje = "Sincronizando comprobantes...";
        try
        {
            var resultado = await repartidor.SincronizarAsync();
            await repartidor.DescargarRutaAsync();
            ActualizarRuta();
            Mensaje = resultado.Rechazadas > 0
                ? "Hay registros rechazados. Revisa el motivo en cada envio; los comprobantes se conservan."
                : automatica && resultado.Aplicadas == 0 ? mensajeAnterior
                : automatica ? "Comprobante confirmado por el servidor."
                : "Sincronizacion completada.";
        }

        catch (SesionRepartidorVencidaException ex)
        {
            await PedirAutenticacionAsync(ex);
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
        MenuAbierto = false;
        if (Ocupado)
        {
            Mensaje = "Hay una operacion en curso. Cuando termine la rueda, vuelve a pulsar Actualizar ruta.";
            return;
        }
        if (RecorridoActivo)
        {
            Mensaje = "Pausa el recorrido antes de actualizar la ruta.";
            return;
        }
        Ocupado = true;
        Mensaje = "Consultando la ruta en el servidor...";
        try
        {
            if (repartidor.ObtenerCantidadSincronizacionesPendientes() > 0)
            {
                Mensaje = "Sincronizando comprobantes antes de actualizar la ruta...";
                var resultado = await repartidor.SincronizarAsync();
                if (resultado.Rechazadas > 0)
                {
                    ActualizarRuta();
                    Mensaje = "Hay registros rechazados. Revisa el motivo en el envio o retiro; las pruebas se conservan.";
                    return;
                }
            }
            await repartidor.DescargarRutaAsync();
            ActualizarRuta();
            var anteriores = repartidor.ObtenerCantidadComprobantesFueraDeRuta();
            var etiquetaComprobantes = anteriores == 1 ? "comprobante anterior" : "comprobantes anteriores";
            Mensaje = anteriores > 0
                ? $"Ruta actualizada. Se conservan {anteriores} {etiquetaComprobantes} sin resolver."
                : "Ruta actualizada.";
        }
        catch (SesionRepartidorVencidaException ex)
        {
            await PedirAutenticacionAsync(ex);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo descargar la ruta");
            if (SoloRutasDeHoy)
            {
                try { ActualizarRuta(); }
                catch (InvalidOperationException actualizarEx)
                {
                    logger.LogWarning(actualizarEx, "No se pudo actualizar la ruta local despues del fallo remoto");
                }
            }
            Mensaje = ex.Message;
        }
        finally { Ocupado = false; }
    }

    [RelayCommand]
    private async Task CerrarSesionAsync()
    {
        MenuAbierto = false;
        if (Ocupado)
            return;
        Ocupado = true;
        try
        {
            await repartidor.CerrarSesionAsync();
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
        if (rutaActual is not null && rutaActual.Id != ruta.Id)
            RecorridoActivo = false;
        rutaActual = ruta;
        OnPropertyChanged(nameof(EstadoInicioRuta));
        entregaParcialHabilitada = ruta.EntregaParcialHabilitada;
        OnPropertyChanged(nameof(UsaEntregaParcial));
        OnPropertyChanged(nameof(UsaEntregaCompleta));
        OnPropertyChanged(nameof(EvidenciaRequeridaTexto));
        Titulo = "Ruta del dia";
        Envios.Clear();
        var esRutaDeHoy = !SoloRutasDeHoy || ruta.Fecha == DateOnly.FromDateTime(DateTime.Today);
        Retiros.Clear();
        foreach (var retiro in repartidor.ObtenerRetiros().Where(retiro =>
            !SoloRutasDeHoy || retiro.Fecha == DateOnly.FromDateTime(DateTime.Today) ||
            retiro.Sincronizacion is EstadoSincronizacion.Pendiente or EstadoSincronizacion.Rechazado))
            Retiros.Add(retiro);
        if (esRutaDeHoy)
        {
            foreach (var envio in ruta.Envios.OrderBy(EnviarAlFinal))
                Envios.Add(envio);
        }
        OnPropertyChanged(nameof(SinEnvios));

        if (!esRutaDeHoy)
            Mensaje = "La ruta guardada corresponde a otra fecha. Se conservan sus comprobantes; actualiza para consultar los envios de hoy.";

        var paradasEntrega = Envios.Select(envio => string.Join(" ", envio.Direccion
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Resumen = $"{paradasEntrega} paradas de entrega | {Envios.Count} envios | {Retiros.Count} retiros | " +
                  $"{Envios.Count(item => item.Estado == EstadoEnvio.Entregado)} entregados | " +
                  $"{Envios.Count(item => item.Sincronizacion == EstadoSincronizacion.Pendiente)} pendientes | " +
                  $"{Envios.Count(item => item.Sincronizacion == EstadoSincronizacion.Rechazado)} rechazados";
        ActualizarVisibles();
        OnPropertyChanged(nameof(PuedeIniciarRuta));
    }

    private static int EnviarAlFinal(Envio envio) =>
        envio.Estado is EstadoEnvio.Entregado or EstadoEnvio.EntregadoParcialmente ||
        envio.Sincronizacion is EstadoSincronizacion.Pendiente or EstadoSincronizacion.Rechazado
            ? 1
            : 0;

    private async Task PedirAutenticacionAsync(SesionRepartidorVencidaException ex)
    {
        logger.LogWarning(ex, "Se requiere reautenticacion; comprobantes conservados");
        Mensaje = ex.Message;
        RecorridoActivo = false;
        await navigation.MostrarLoginAsync();
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
