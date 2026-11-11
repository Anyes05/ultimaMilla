using Microsoft.Extensions.DependencyInjection;

namespace Plataforma.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider services;

	public App(IServiceProvider services)
	{
		this.services = services;
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Las paginas usan recursos XAML que deben existir antes de construir el Shell.
		var window = new Window(services.GetRequiredService<AppShell>());
		var vm = services.GetRequiredService<ViewModels.RutaViewModel>();
		var activa = false;
		async Task ActualizarConexionAsync(bool disponible)
		{
			await MainThread.InvokeOnMainThreadAsync(async () =>
			{
				vm.ConexionSimulada = disponible;
				if (!activa || !disponible) return;
				await vm.AutoSincronizarAsync();
				await vm.RefrescarRutaAlMostrarAsync();
			});
		}
		async void Conexion(object? sender, ConnectivityChangedEventArgs e) =>
			await ActualizarConexionAsync(e.NetworkAccess == NetworkAccess.Internet);
		async void Activar(object? sender, EventArgs e)
		{
			activa = true;
			vm.SoloRutasDeHoy = true;
			await ActualizarConexionAsync(Connectivity.Current.NetworkAccess == NetworkAccess.Internet);
		}
		window.Activated += Activar;
		window.Resumed += Activar;
		window.Stopped += (_, _) => { activa = false; };
		Connectivity.Current.ConnectivityChanged += Conexion;
		window.Destroying += (_, _) =>
		{
			activa = false;
			Connectivity.Current.ConnectivityChanged -= Conexion;
		};
		return window;
	}
}