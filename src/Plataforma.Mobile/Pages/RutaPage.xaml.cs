using Plataforma.Mobile.ViewModels;
using Plataforma.Mobile.Models;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
using Microsoft.Extensions.Logging;

namespace Plataforma.Mobile.Pages;
//ll
public partial class RutaPage : ContentPage
{
    private readonly RutaViewModel viewModel;
    private readonly FirmaDrawable firma = new();
    private readonly ILogger<RutaPage> logger;

    public RutaPage(RutaViewModel viewModel, ILogger<RutaPage> logger)
    {
        this.logger = logger;
        this.viewModel = viewModel;
        BindingContext = viewModel;
        InitializeComponent();
        LienzoFirma.Drawable = firma;
    }

    private void IniciarFirma(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;
        if (firma.Trazos.Sum(t => t.Count) >= 1500) return;
        firma.Trazos.Add([e.Touches[0]]);
        LienzoFirma.Invalidate();
    }

    private void DibujarFirma(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || firma.Trazos.Count == 0 || firma.Trazos.Sum(t => t.Count) >= 1500) return;
        firma.Trazos[^1].Add(e.Touches[0]);
        LienzoFirma.Invalidate();
    }

    private void BorrarFirma(object? sender, EventArgs e)
    {
        firma.Trazos.Clear();
        LienzoFirma.Invalidate();
    }

    private async void GuardarEvidencia(object? sender, EventArgs e)
    {
        if (viewModel.Ocupado || !viewModel.HaySeleccion) return;
        if (!await ObtenerGpsAsync()) return;
        viewModel.Firma = firma.Exportar();
        viewModel.GuardarEntregaCommand.Execute(null);
        if (!viewModel.HaySeleccion)
        {
            BorrarFirma(null, EventArgs.Empty);
            EstadoEvidencia.Text = "";
        }
    }

    private void LimpiarVistaPreviaFoto()
    {
        VistaPreviaFoto.Source = null;
        VistaPreviaFoto.IsVisible = false;
    }

    private async Task<bool> ObtenerGpsAsync()
    {
        viewModel.Ocupado = true;
        viewModel.Latitud = viewModel.Longitud = null;
        EstadoEvidencia.Text = "Obteniendo ubicacion. Espera antes de guardar.";
        try
        {
            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(20)));
            if (location is null) throw new InvalidOperationException("No se obtuvo una posicion GPS.");
            viewModel.Latitud = location.Latitude;
            viewModel.Longitud = location.Longitude;
            EstadoEvidencia.Text = $"GPS {location.Latitude:F5}, {location.Longitude:F5}; hora {location.Timestamp:HH:mm:ss}";
            return true;
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException
            or PermissionException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "No se pudo capturar ubicacion para el comprobante");
            EstadoEvidencia.Text = ex switch
            {
                PermissionException => "Permite el acceso a la ubicacion en Ajustes y vuelve a guardar. Tu formulario se conserva.",
                FeatureNotEnabledException => "Activa la ubicacion del telefono y vuelve a guardar. Tu formulario se conserva.",
                FeatureNotSupportedException => "Este dispositivo no permite obtener la ubicacion. Contacta al operador; el comprobante no se guardo.",
                _ => "No pudimos obtener tu ubicacion. Busca un lugar con mejor senal GPS y vuelve a guardar. Tu formulario se conserva."
            };
            return false;
        }
        finally { viewModel.Ocupado = false; }
    }

    private async void TomarFoto(object? sender, EventArgs e)
    {
        if (viewModel.Ocupado) return;
        viewModel.Ocupado = true;
        viewModel.Foto = null;
        LimpiarVistaPreviaFoto();
        EstadoEvidencia.Text = "Sin foto adjunta.";
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
                throw new InvalidOperationException("Este dispositivo no admite captura.");
            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo is null) return;
#if ANDROID
            using var boundsStream = await photo.OpenReadAsync();
            var bounds = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeStream(boundsStream, null, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0) throw new InvalidOperationException("Foto invalida.");
            var sample = 1;
            while (Math.Max(bounds.OutWidth, bounds.OutHeight) / sample > 1600) sample *= 2;
            using var stream = await photo.OpenReadAsync();
            using var bitmap = Android.Graphics.BitmapFactory.DecodeStream(stream, null,
                new Android.Graphics.BitmapFactory.Options { InSampleSize = sample })
                ?? throw new InvalidOperationException("No se pudo procesar la foto.");
            byte[]? jpeg = null;
            foreach (var quality in new[] { 80, 65, 50, 35, 20 })
            {
                using var output = new MemoryStream();
                bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, quality, output);
                if (output.Length <= 512 * 1024) { jpeg = output.ToArray(); break; }
            }
            if (jpeg is null) throw new InvalidOperationException("La foto no se pudo comprimir a 512 KiB. Repite con menor detalle.");
            viewModel.Foto = Convert.ToBase64String(jpeg);
            var imagen = jpeg;
            VistaPreviaFoto.Source = ImageSource.FromStream(() => new MemoryStream(imagen, writable: false));
            VistaPreviaFoto.IsVisible = true;
            EstadoEvidencia.Text = $"JPEG adjunto: {jpeg.Length / 1024} KiB";
#else
            throw new InvalidOperationException("Compresion JPEG implementada en Android; esta plataforma aun no esta validada.");
#endif
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo capturar o procesar la foto");
            EstadoEvidencia.Text = ex is PermissionException
                ? "Permite el acceso a la camara en Ajustes y vuelve a tomar la foto."
                : "No pudimos adjuntar la foto. Vuelve a tomarla; el resto del formulario se conserva.";
        }
        finally { viewModel.Ocupado = false; }
    }

    private async void EscanearCarga(object? sender, EventArgs e)
    {
        if (viewModel.Ocupado || sender is not Button { CommandParameter: Envio envio }) return;
        try
        {
            var codigo = await EscanearCodigoAsync("Escanear carga", $"Escanea {envio.NumeroSeguimiento}");
            if (codigo is not null) viewModel.RegistrarCarga(envio, codigo);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo abrir o completar el escaneo");
            viewModel.Mensaje = "No pudimos completar el escaneo. Revisa el permiso de camara y vuelve a intentarlo. No se registro la carga.";
        }
    }

    private async void EscanearBultoEntrega(object? sender, EventArgs e)
    {
        if (viewModel.Ocupado || sender is not Button { CommandParameter: BultoEntregaItem item }) return;
        try
        {
            var codigo = await EscanearCodigoAsync("Entregar bulto", $"Escanea el codigo {item.Bulto.Codigo}");
            if (codigo is not null) viewModel.RegistrarBultoEntregado(item.Bulto.Id, codigo);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo escanear el bulto de entrega");
            viewModel.Mensaje = "No se pudo escanear el bulto. No se marco como entregado.";
        }
    }

    private async Task<string?> EscanearCodigoAsync(string titulo, string instruccion)
    {
        if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
            throw new InvalidOperationException("Permiso de camara denegado.");

        viewModel.Ocupado = true;
        var resultado = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var camera = new CameraBarcodeReaderView
            {
                IsDetecting = true,
                HeightRequest = 360,
                Options = new BarcodeReaderOptions
                {
                    Formats = BarcodeFormats.All,
                    AutoRotate = true,
                    TryHarder = true,
                    Multiple = false
                }
            };
            var cerrar = new Button { Text = "Cancelar escaneo" };
            var manual = new Button { Text = "Ingresar codigo manualmente" };
            var page = new ContentPage
            {
                Title = titulo,
                Content = new VerticalStackLayout
                {
                    Children = { new Label { Text = instruccion }, camera, manual, cerrar }
                }
            };
            var terminado = false;
            async Task Terminar(string? codigo)
            {
                if (terminado) return;
                terminado = true;
                camera.IsDetecting = false;
                try { await Navigation.PopModalAsync(); }
                finally { resultado.TrySetResult(string.IsNullOrWhiteSpace(codigo) ? null : codigo.Trim()); }
            }
            camera.BarcodesDetected += (_, args) =>
                MainThread.BeginInvokeOnMainThread(async () => await Terminar(args.Results.FirstOrDefault()?.Value));
            cerrar.Clicked += async (_, _) => await Terminar(null);
            manual.Clicked += async (_, _) =>
            {
                camera.IsDetecting = false;
                var codigo = await page.DisplayPromptAsync("Ingreso manual", instruccion);
                if (codigo is not null) await Terminar(codigo);
                else camera.IsDetecting = true;
            };
            page.Disappearing += (_, _) =>
            {
                camera.IsDetecting = false;
                if (!terminado) resultado.TrySetResult(null);
            };
            await Navigation.PushModalAsync(page);
            return await resultado.Task;
        }
        finally { viewModel.Ocupado = false; }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        viewModel.PantallaActiva = true;
        viewModel.SoloRutasDeHoy = true;
        viewModel.PropertyChanged += CambioSeleccion;
        await viewModel.RefrescarRutaAlMostrarAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.PantallaActiva = false;
        viewModel.PropertyChanged -= CambioSeleccion;
        base.OnDisappearing();
    }

    private void CambioSeleccion(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RutaViewModel.EnvioSeleccionado)) return;
        LimpiarVistaPreviaFoto();
        BorrarFirma(null, EventArgs.Empty);
        EstadoEvidencia.Text = "";
    }

    protected override bool OnBackButtonPressed()
    {
        if (viewModel.HaySeleccion)
        {
            viewModel.CancelarEntregaCommand.Execute(null);
            return true;
        }

        if (viewModel.RecorridoActivo)
        {
            viewModel.PausarRecorridoCommand.Execute(null);
            return true;
        }
        return base.OnBackButtonPressed();
    }
}
