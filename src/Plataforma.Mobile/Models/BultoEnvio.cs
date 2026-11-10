namespace Plataforma.Mobile.Models;

using Plataforma.Domain;

public sealed record BultoEnvio(
    Guid Id,
    string Codigo,
    decimal PesoKg,
    EstadoBulto Estado,
    string? MotivoNoEntrega = null)
{
    public bool PuedeEntregarse
    {
        get
        {
            var estadoDeRuta = Estado is EstadoBulto.Pendiente or EstadoBulto.PendienteNuevaVisita
                ? EstadoBulto.EnRuta
                : Estado;
            return TransicionesBulto.DestinosDesde(estadoDeRuta)
                .Contains(EstadoBulto.Entregado);
        }
    }
}

public sealed record ResultadoBultoEntrega(
    Guid BultoId,
    EstadoBulto Estado,
    string? MotivoNoEntrega = null,
    string? Foto = null,
    string? Firma = null);

public static class CatalogoMotivosNoEntrega
{
    public static IReadOnlyList<string> Predeterminados { get; } =
        ["Ausente", "Destinatario ausente", "Rechazo del destinatario", "Domicilio no encontrado", "Bulto danado"];
}