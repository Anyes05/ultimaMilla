using Microsoft.Extensions.Logging;
using Plataforma.Mobile.Pages;
using Plataforma.Mobile.Services;
using Plataforma.Mobile.ViewModels;
using Plataforma.Mobile.Data;
using ZXing.Net.Maui.Controls;

namespace Plataforma.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseBarcodeReader()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<ITokenRepartidor, TokenRepartidorSeguro>();
		builder.Services.AddSingleton(proveedor => new RepartidorHttpService(
			new HttpClient { BaseAddress = new Uri(Preferences.Default.Get("repartidor.api.url", "https://api.tallernet.cloud-ip.cc/")), Timeout = TimeSpan.FromSeconds(30) },
			new RepartidorLocalData(Path.Combine(FileSystem.AppDataDirectory, "repartidor-api.db"),
				new SnapshotCipher(ClaveSnapshotSegura.Obtener)),
			proveedor.GetRequiredService<ITokenRepartidor>()));
		builder.Services.AddSingleton<IRepartidorService>(proveedor =>
			proveedor.GetRequiredService<RepartidorHttpService>());
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
