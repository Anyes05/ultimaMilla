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
		var rastreo = services.GetRequiredService<Services.IRastreoUbicacion>();
		var activa = false;
		async Task ActualizarConexionAsync(bool disponible)
		{
			await MainThread.InvokeOnMainThreadAsync(async () =>
			{
				vm.ConexionSimulada = disponible;
				if (!disponible) return;
				if (!activa)
				{
					await rastreo.EnviarPendientesAsync();
					return;
				}
				await vm.AutoSincronizarAsync();
				await rastreo.EnviarPendientesAsync();
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
			if (vm.PantallaActiva && !vm.HaySeleccion) await rastreo.ActualizarAsync();
		}
		async void CambioRuta(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(vm.Ocupado) && !vm.Ocupado && activa)
			{
				await rastreo.ActualizarAsync(vm.PantallaActiva && !vm.HaySeleccion);
				await rastreo.EnviarPendientesAsync();
			}
		}
		vm.PropertyChanged += CambioRuta;
		window.Activated += Activar;
		window.Resumed += Activar;
		window.Stopped += (_, _) => { activa = false; };
		Connectivity.Current.ConnectivityChanged += Conexion;
		window.Destroying += (_, _) =>
		{
			activa = false;
			vm.PropertyChanged -= CambioRuta;
			Connectivity.Current.ConnectivityChanged -= Conexion;
		};
		return window;
	}
}