namespace Plataforma.Mobile;

public partial class AppShell : Shell
{
	public AppShell(Pages.LoginPage loginPage, Pages.RutaPage rutaPage)
	{
		InitializeComponent();
		Items.Add(new ShellContent { Route = "login", Title = "Acceso", Content = loginPage });
		Items.Add(new ShellContent { Route = "ruta", Title = "Ruta del dia", Content = rutaPage });
	}
}
