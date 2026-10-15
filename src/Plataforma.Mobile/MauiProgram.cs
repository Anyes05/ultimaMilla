using Microsoft.Extensions.Logging;
using Plataforma.Mobile.Pages;
using Plataforma.Mobile.Services;
using Plataforma.Mobile.ViewModels;
using Plataforma.Mobile.Data;

namespace Plataforma.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton(new RepartidorLocalData(
			Path.Combine(FileSystem.AppDataDirectory, "repartidor-demo.db")));
		builder.Services.AddSingleton<IRepartidorService, RepartidorDemoService>();
		builder.Services.AddSingleton<INavigationService, ShellNavigationService>();
		builder.Services.AddSingleton<LoginViewModel>();
		builder.Services.AddSingleton<RutaViewModel>();
		builder.Services.AddSingleton<LoginPage>();
		builder.Services.AddSingleton<RutaPage>();
		builder.Services.AddSingleton<AppShell>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
