namespace Plataforma.Mobile.Services;

public sealed class ShellNavigationService : INavigationService
{
    public Task MostrarRutaAsync() => Shell.Current.GoToAsync("//ruta");
    public Task MostrarLoginAsync() => Shell.Current.GoToAsync("//login");
}
