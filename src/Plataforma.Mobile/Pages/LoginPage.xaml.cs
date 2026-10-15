using Plataforma.Mobile.ViewModels;

namespace Plataforma.Mobile.Pages;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        BindingContext = viewModel;
        InitializeComponent();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await ClaveEntry.HideSoftInputAsync(CancellationToken.None);
        EmailEntry.Unfocus();
        ClaveEntry.Unfocus();
    }
}
