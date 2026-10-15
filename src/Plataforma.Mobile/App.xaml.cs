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
		return new Window(services.GetRequiredService<AppShell>());
	}
}