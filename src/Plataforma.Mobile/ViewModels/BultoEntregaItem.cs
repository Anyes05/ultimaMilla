using CommunityToolkit.Mvvm.ComponentModel;
using Plataforma.Mobile.Models;
using EstadoBulto = Plataforma.Domain.EstadoBulto;

namespace Plataforma.Mobile.ViewModels;

public partial class BultoEntregaItem : ObservableObject
{
    public BultoEnvio Bulto { get; }
    public IReadOnlyList<string> MotivosNoEntrega { get; }
    public bool PuedeEntregarse => Bulto.PuedeEntregarse;
    public bool RequiereMotivo => PuedeEntregarse && !Entregado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiereMotivo))]
    private bool entregado;

    [ObservableProperty]
    private string? motivoNoEntrega;

    public BultoEntregaItem(BultoEnvio bulto, IReadOnlyList<string> motivosNoEntrega)
    {
        Bulto = bulto;
        MotivosNoEntrega = motivosNoEntrega;
        entregado = bulto.Estado == EstadoBulto.Entregado;
        motivoNoEntrega = bulto.MotivoNoEntrega;
    }
}