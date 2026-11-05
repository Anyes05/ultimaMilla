namespace Plataforma.Mobile;

public partial class AppShell : Shell
{
	public AppShell(Pages.LoginPage loginPage, Pages.RutaPage rutaPage,
		Services.IRepartidorService repartidor, ViewModels.LoginViewModel loginViewModel)
	{
		InitializeComponent();
		var login = new ShellContent { Route = "login", Title = "Acceso", Content = loginPage };
		var ruta = new ShellContent { Route = "ruta", Title = "Ruta del dia", Content = rutaPage };
		var restaurar = false;
		try
		{
			restaurar = repartidor.HaySesion;
		}
		catch (InvalidOperationException ex)
		{
			loginViewModel.Mensaje = $"No se pudo recuperar la sesion: {ex.Message}";
		}
		if (restaurar)
		{
			Items.Add(ruta);
			Items.Add(login);
		}
		else
		{
			Items.Add(login);
			Items.Add(ruta);
		}
	}
}
