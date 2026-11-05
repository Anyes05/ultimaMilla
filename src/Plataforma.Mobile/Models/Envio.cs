namespace Plataforma.Mobile.Models;

public enum EstadoEnvio
{
    EnTransito,
    Entregado,
    Extraviado,
    Admitido,
    EnDeposito,
    AsignadoARuta,
    NoEntregado,
    Reprogramado,
    EnDevolucion,
    Devuelto
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
    string? MotivoRechazo,
    IReadOnlyList<EntregaPendiente>? Historial = null)
{
    public string EstadoServidorTexto => Estado switch
    {
        EstadoEnvio.Entregado => "Entrega registrada",
        EstadoEnvio.EnTransito => "En camino",
        EstadoEnvio.AsignadoARuta => "Pendiente de carga",
        EstadoEnvio.NoEntregado => "Intento fallido registrado",
        EstadoEnvio.EnDeposito => "En deposito",
        EstadoEnvio.EnDevolucion => "En devolucion",
        EstadoEnvio.Admitido => "Recibido",
        EstadoEnvio.Extraviado => "Extraviado",
        EstadoEnvio.Reprogramado => "Reprogramado",
        EstadoEnvio.Devuelto => "Devuelto",
        _ => "Estado no disponible"
    };

    public string EstadoLocalTexto => Sincronizacion switch
    {
        EstadoSincronizacion.Pendiente => "Registro pendiente de sincronizar",
        EstadoSincronizacion.Sincronizado => "Sincronizado",
        EstadoSincronizacion.Rechazado => $"Registro rechazado: {MotivoRechazo}. Comprobante conservado.",
        _ => string.Empty
    };

    public string ComprobanteTexto => Entrega is null
        ? string.Empty
        : $"Recibio: {Entrega.NombreReceptor} | {Entrega.Fecha:dd/MM HH:mm} | {Entrega.Observaciones}";

    public bool PuedeEntregar => Estado == EstadoEnvio.EnTransito && Entrega is null;
    public bool PuedeCargar => Estado == EstadoEnvio.AsignadoARuta && Entrega is null;
}
