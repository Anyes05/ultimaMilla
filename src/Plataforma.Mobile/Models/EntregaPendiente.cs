namespace Plataforma.Mobile.Models;

public sealed record EntregaPendiente(
    Guid Id,
    Guid EnvioId,
    string NombreReceptor,
    string Observaciones,
    DateTimeOffset Fecha);

public sealed record ResultadoSincronizacion(int Aplicadas, int Rechazadas, int Pendientes);
