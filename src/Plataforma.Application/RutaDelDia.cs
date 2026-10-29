using Plataforma.Domain;

namespace Plataforma.Application;

public sealed record OperadorVisible(string Nombre, string ColorMarca, int MaximoIntentos)
{
    public static OperadorVisible Desde(Operador operador) =>
        new(operador.Nombre, operador.ColorMarca, operador.MaximoIntentos);
}

public sealed record EnvioDeRuta(
    Guid Id,
    string NumeroSeguimiento,
    string Destinatario,
    string DireccionEntrega,
    string EmailDestinatario,
    EstadoEnvio Estado,
    decimal TarifaCalculada,
    int IntentosRealizados)
{
    public static EnvioDeRuta Desde(Envio envio) =>
        new(
            envio.Id,
            envio.NumeroSeguimiento,
            envio.Destinatario,
            envio.DireccionEntrega,
            envio.EmailDestinatario,
            envio.Estado,
            envio.TarifaCalculada,
            envio.IntentosRealizados);
}

public sealed record RutaDelDia(Guid Id, DateOnly Fecha, OperadorVisible Operador, IReadOnlyList<EnvioDeRuta> Envios);

/// <summary>Comprobante offline del móvil (contrato de sincronización).</summary>
public sealed record ComprobanteEntrega(
    Guid EventoId,
    Guid EnvioId,
    EstadoEnvio Estado,
    string NombreReceptor,
    string Observaciones,
    DateTimeOffset FechaDispositivo,
    string? Foto = null,
    string? Firma = null,
    decimal? Latitud = null,
    decimal? Longitud = null,
    string? CodigoEscaneado = null);

public sealed record EventoAplicado(Guid EnvioId, EstadoEnvio Estado, int IntentosRealizados);

public sealed record EventoRechazado(Guid EnvioId, string Motivo, EstadoEnvio? EstadoActual = null);

public sealed record ResultadoSincronizacion(
    IReadOnlyList<EventoAplicado> Aplicados,
    IReadOnlyList<EventoRechazado> Rechazados);

/// <summary>Sesión del repartidor según contrato de login.</summary>
public sealed record SesionRepartidor(
    Guid RepartidorId,
    Guid OperadorId,
    string Nombre,
    string Rol,
    string Token);
