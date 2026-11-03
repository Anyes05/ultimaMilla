namespace Plataforma.Worker.Push;

/// <summary>
/// Envío de una notificación push a un dispositivo (opcional 7.3). Es el único punto que conoce el proveedor:
/// Álvaro implementa <c>EmisorPushFcm</c> (FCM HTTP v1); colas, reintentos e idempotencia quedan en el worker.
/// Ver docs/propuestas/push-reasignacion.md.
/// </summary>
public interface IEmisorPush
{
    Task<ResultadoPush> EnviarAsync(MensajePush mensaje, CancellationToken cancellationToken);
}

/// <param name="Datos">Claves para la app al tocar la notificación: tipo=reasignacion, envioId, rutaId.</param>
public sealed record MensajePush(
    string Token,
    string Plataforma,
    string Titulo,
    string Cuerpo,
    IReadOnlyDictionary<string, string> Datos);

public enum ResultadoPush
{
    Enviado,

    /// <summary>El proveedor rechaza el token (UNREGISTERED / INVALID_ARGUMENT): la app se desinstaló o renovó el token.</summary>
    TokenInvalido,

    /// <summary>429, 5xx o red: se reintenta con espera.</summary>
    ErrorTransitorio
}

/// <summary>
/// Emisor de prueba mientras no hay FCM: registra el push en el log y lo da por enviado.
/// Permite probar el recorrido completo (evento → cola → worker → push) sin credenciales.
/// </summary>
public sealed class EmisorPushRegistro(ILogger<EmisorPushRegistro> logger) : IEmisorPush
{
    public Task<ResultadoPush> EnviarAsync(MensajePush mensaje, CancellationToken cancellationToken)
    {
        logger.LogInformation("Push (registro, sin FCM) a {Plataforma} ...{Token}: {Titulo} — {Cuerpo}",
            mensaje.Plataforma, mensaje.Token[^Math.Min(6, mensaje.Token.Length)..], mensaje.Titulo, mensaje.Cuerpo);
        return Task.FromResult(ResultadoPush.Enviado);
    }
}
