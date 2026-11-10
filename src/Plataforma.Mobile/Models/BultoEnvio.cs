namespace Plataforma.Mobile.Models;

public enum EstadoBulto
{
    Pendiente,
    EnRuta,
    Entregado,
    NoEntregado,
    PendienteNuevaVisita,
    Devuelto,
    Extraviado
}

public sealed record BultoEnvio(
    Guid Id,
    string Codigo,
    decimal PesoKg,
    EstadoBulto Estado,
    string? MotivoNoEntrega = null)
{
    public bool PuedeEntregarse => Estado is
        EstadoBulto.EnRuta or EstadoBulto.Pendiente or EstadoBulto.NoEntregado or EstadoBulto.PendienteNuevaVisita;
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