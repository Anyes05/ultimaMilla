namespace Plataforma.Mobile.Models;

public sealed record EntregaPendiente(
    Guid Id,
    Guid EnvioId,
    string NombreReceptor,
    string Observaciones,
    DateTimeOffset Fecha,
    EstadoEnvio Estado = EstadoEnvio.Entregado,
    string? Foto = null,
    string? Firma = null,
    double? Latitud = null,
    double? Longitud = null,
    string? CodigoEscaneado = null,
    string? NumeroSeguimiento = null,
    string? Destinatario = null,
    IReadOnlyList<ResultadoBultoEntrega>? Bultos = null)
{
    public string FechaTexto => Fecha.ToString("dd/MM/yyyy HH:mm");
}

public sealed record ResultadoSincronizacion(int Aplicadas, int Rechazadas, int Pendientes);
