using Plataforma.Mobile.ViewModels;

namespace Plataforma.Mobile.Pages;

public partial class RutaPage : ContentPage
{
    private readonly RutaViewModel viewModel;

    public RutaPage(RutaViewModel viewModel)
    {
        this.viewModel = viewModel;
        BindingContext = viewModel;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.CargarCommand.Execute(null);
    }

    protected override bool OnBackButtonPressed()
    {
        if (viewModel.HaySeleccion)
        {
            viewModel.CancelarEntregaCommand.Execute(null);
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
