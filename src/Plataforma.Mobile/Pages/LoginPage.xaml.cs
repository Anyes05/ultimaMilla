using Plataforma.Mobile.ViewModels;

namespace Plataforma.Mobile.Pages;

public partial class LoginPage : ContentPage
{
    private readonly Services.RepartidorHttpService api;
    public LoginPage(LoginViewModel viewModel, Services.RepartidorHttpService api)
    {
        this.api = api;
        BindingContext = viewModel;
        InitializeComponent();
    }

    private async void ConfigurarServidor(object? sender, EventArgs e)
    {
        var url = await DisplayPromptAsync("Servidor API", "HTTPS o Compose local. Requiere cerrar sesion sin pendientes.",
            initialValue: Preferences.Default.Get("repartidor.api.url", "https://api.tallernet.cloud-ip.cc/"));
        if (url is null) return;
        try
        {
            api.ConfigurarEndpoint(url);
            Preferences.Default.Set("repartidor.api.url", url.TrimEnd('/') + "/");
            await DisplayAlertAsync("Configuracion", "Servidor actualizado.", "OK");
        }
        catch (InvalidOperationException ex) { await DisplayAlertAsync("Configuracion", ex.Message, "OK"); }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await ClaveEntry.HideSoftInputAsync(CancellationToken.None);
        UsuarioEntry.Unfocus();
        ClaveEntry.Unfocus();
    }
}
