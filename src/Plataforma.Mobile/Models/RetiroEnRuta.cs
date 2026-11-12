using Plataforma.Domain;

namespace Plataforma.Mobile.Models;

public sealed record RetiroPendiente(Guid EventoId, Guid RetiroId, Guid RutaId,
    DateTimeOffset FechaDispositivo, IReadOnlyList<string> CodigosBultos,
    string NombreResponsable, string Foto, string Firma, double Latitud, double Longitud);

public sealed record RetiroEnRuta(Guid Id, Guid RutaId, DateOnly Fecha, string Direccion,
    string Contacto, int CantidadBultos, int? PosicionEnRuta, EstadoRetiro Estado,
    RetiroPendiente? Comprobante = null, EstadoSincronizacion Sincronizacion = EstadoSincronizacion.SinCambios,
    string? MotivoRechazo = null)
{
    public bool PuedeRetirar => Comprobante is null && Estado is EstadoRetiro.Programado or EstadoRetiro.EnCurso;
    public string ParadaTexto => PosicionEnRuta is int posicion
        ? $"Retiro despues de {posicion} entregas | {CantidadBultos} bultos | {Fecha:dd/MM}"
        : $"Retiro al final de la ruta | {CantidadBultos} bultos | {Fecha:dd/MM}";
    public string EstadoTexto => Sincronizacion switch
    {
        EstadoSincronizacion.Pendiente => "Retiro guardado; pendiente de sincronizar",
        EstadoSincronizacion.Rechazado => $"Rechazado: {MotivoRechazo}. Evidencia conservada.",
        EstadoSincronizacion.Sincronizado => "Retiro confirmado",
        _ => Estado.ToString()
    };
}