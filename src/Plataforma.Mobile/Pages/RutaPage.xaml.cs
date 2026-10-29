using Plataforma.Mobile.ViewModels;
using Plataforma.Mobile.Models;
using ZXing.Net.Maui.Controls;

namespace Plataforma.Mobile.Pages;

public partial class RutaPage : ContentPage
{
    private readonly RutaViewModel viewModel;
    private readonly FirmaDrawable firma = new();

    public RutaPage(RutaViewModel viewModel)
    {
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

    private void GuardarEvidencia(object? sender, EventArgs e)
    {
        viewModel.Firma = firma.Exportar();
        viewModel.GuardarEntregaCommand.Execute(null);
        if (!viewModel.HaySeleccion)
        {
            BorrarFirma(null, EventArgs.Empty);
            EstadoEvidencia.Text = "";
        }
    }

    private async void CapturarGps(object? sender, EventArgs e)
    {
        if (viewModel.Ocupado) return;
        viewModel.Ocupado = true;
        viewModel.Latitud = viewModel.Longitud = null;
        try
        {
            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(20)));
            if (location is null) throw new InvalidOperationException("No se obtuvo una posicion GPS.");
            viewModel.Latitud = location.Latitude;
            viewModel.Longitud = location.Longitude;
            EstadoEvidencia.Text = $"GPS {location.Latitude:F5}, {location.Longitude:F5}; hora {location.Timestamp:HH:mm:ss}";
        }
        catch (Exception ex) { EstadoEvidencia.Text = $"GPS no capturado: {ex.Message}. Puedes reintentar; no se invento una posicion."; }
        finally { viewModel.Ocupado = false; }
    }

    private async void TomarFoto(object? sender, EventArgs e)
    {
        if (viewModel.Ocupado) return;
        viewModel.Ocupado = true;
        viewModel.Foto = null;
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
            EstadoEvidencia.Text = $"JPEG adjunto: {jpeg.Length / 1024} KiB";
#else
            throw new InvalidOperationException("Compresion JPEG implementada en Android; esta plataforma aun no esta validada.");
#endif
        }
        catch (Exception ex) { EstadoEvidencia.Text = $"Foto no adjuntada: {ex.Message}"; }
        finally { viewModel.Ocupado = false; }
    }

    private async void EscanearCarga(object? sender, EventArgs e)
    {
        if (viewModel.Ocupado || sender is not Button { CommandParameter: Envio envio }) return;
        if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
        {
            viewModel.Mensaje = "Permiso de camara denegado. No se registro carga.";
            return;
        }
        viewModel.Ocupado = true;
        try
        {
            var camera = new CameraBarcodeReaderView { IsDetecting = true, HeightRequest = 360 };
            var cerrar = new Button { Text = "Cancelar escaneo" };
            var manual = new Button { Text = "Ingresar codigo manualmente (si camara falla)" };
            var page = new ContentPage
            {
                Title = "Escanear carga",
                Content = new VerticalStackLayout
                {
                    Children = { new Label { Text = $"Escanea {envio.NumeroSeguimiento}" }, camera, manual, cerrar }
                }
            };
            var terminado = false;
            async Task Terminar(string? codigo)
            {
                if (terminado) return;
                terminado = true;
                camera.IsDetecting = false;
                await Navigation.PopModalAsync();
                viewModel.Ocupado = false;
                if (codigo is not null) viewModel.RegistrarCarga(envio, codigo);
            }
            camera.BarcodesDetected += (_, args) =>
                MainThread.BeginInvokeOnMainThread(async () => await Terminar(args.Results.FirstOrDefault()?.Value));
            cerrar.Clicked += async (_, _) => await Terminar(null);
            manual.Clicked += async (_, _) =>
            {
                camera.IsDetecting = false;
                var codigo = await page.DisplayPromptAsync("Carga manual", "Codigo exacto de seguimiento");
                if (codigo is not null) await Terminar(codigo.Trim());
                else camera.IsDetecting = true;
            };
            page.Disappearing += (_, _) => { camera.IsDetecting = false; viewModel.Ocupado = false; };
            await Navigation.PushModalAsync(page);
        }
        catch (Exception ex) { viewModel.Ocupado = false; viewModel.Mensaje = $"No se pudo escanear: {ex.Message}"; }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.PantallaActiva = true;
        viewModel.PropertyChanged += CambioSeleccion;
        viewModel.CargarCommand.Execute(null);
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

        return base.OnBackButtonPressed();
    }
}
