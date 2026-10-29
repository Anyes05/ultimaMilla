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
		var timer = Dispatcher.CreateTimer();
		timer.Interval = TimeSpan.FromSeconds(45);
		var activa = false;
		async void Sincronizar(object? sender, EventArgs e)
		{
			if (!activa || Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
			await MainThread.InvokeOnMainThreadAsync(vm.AutoSincronizarAsync);
		}
		void Conexion(object? sender, ConnectivityChangedEventArgs e) => Sincronizar(sender, e);
		void Activar(object? sender, EventArgs e)
		{
			activa = true;
			timer.Start();
			Sincronizar(sender, e);
		}
		timer.Tick += Sincronizar;
		window.Activated += Activar;
		window.Resumed += Activar;
		window.Stopped += (_, _) => { activa = false; timer.Stop(); };
		Connectivity.Current.ConnectivityChanged += Conexion;
		window.Destroying += (_, _) =>
		{
			activa = false;
			timer.Stop();
			timer.Tick -= Sincronizar;
			Connectivity.Current.ConnectivityChanged -= Conexion;
		};
		return window;
	}
}