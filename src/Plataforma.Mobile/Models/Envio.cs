namespace Plataforma.Mobile.Models;

public enum EstadoEnvio
{
    EnTransito,
    Entregado,
    Extraviado
}

public enum EstadoSincronizacion
{
    SinCambios,
    Pendiente,
    Sincronizado,
    Rechazado
}

public sealed record Envio(
    Guid Id,
    string NumeroSeguimiento,
    string Destinatario,
    string Direccion,
    EstadoEnvio Estado,
    EntregaPendiente? Entrega,
    EstadoSincronizacion Sincronizacion,
    string? MotivoRechazo)
{
    public string EstadoServidorTexto => $"Estado conocido del servidor: {Estado}";

    public string EstadoLocalTexto => Sincronizacion switch
    {
        EstadoSincronizacion.Pendiente => "Entrega guardada en SQLite: pendiente",
        EstadoSincronizacion.Sincronizado => "Entrega aceptada por el servidor simulado",
        EstadoSincronizacion.Rechazado => $"Rechazada: {MotivoRechazo} Datos locales conservados.",
        _ => "Sin entrega local"
    };

    public string ComprobanteTexto => Entrega is null
        ? string.Empty
        : $"Recibio: {Entrega.NombreReceptor} | {Entrega.Fecha:dd/MM HH:mm} | {Entrega.Observaciones}";

    public bool PuedeEntregar => Estado == EstadoEnvio.EnTransito && Entrega is null;
}
