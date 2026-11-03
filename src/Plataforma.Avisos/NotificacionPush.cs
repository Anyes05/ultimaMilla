namespace Plataforma.Avisos;

public enum EstadoNotificacionPush
{
    Pendiente,
    Enviada,
    TokenInvalido,
    Fallida
}

/// <summary>
/// Un push de reasignación urgente a un dispositivo (opcional 7.3). Es el registro de idempotencia:
/// (EventId, Token) es único, así que el mismo evento repetido no llega dos veces al mismo teléfono (letra 6.8).
/// </summary>
public sealed class NotificacionPush
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid RepartidorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public string Plataforma { get; private set; } = string.Empty;
    public EstadoNotificacionPush Estado { get; private set; }
    public int Intentos { get; private set; }
    public string? UltimoError { get; private set; }
    public DateTime CreadaEn { get; private set; }
    public DateTime ActualizadaEn { get; private set; }

    private NotificacionPush()
    {
    }

    public static NotificacionPush Crear(
        Guid eventId, Guid operadorId, Guid repartidorId, Guid envioId, string token, string plataforma, DateTime ahoraUtc)
    {
        if (eventId == Guid.Empty || operadorId == Guid.Empty || repartidorId == Guid.Empty)
            throw new ArgumentException("La notificación necesita evento, operador y repartidor.");
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("La notificación necesita el token del dispositivo.");

        return new NotificacionPush
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            OperadorId = operadorId,
            RepartidorId = repartidorId,
            EnvioId = envioId,
            Token = token,
            Plataforma = plataforma,
            Estado = EstadoNotificacionPush.Pendiente,
            CreadaEn = ahoraUtc,
            ActualizadaEn = ahoraUtc
        };
    }

    /// <summary>Solo se intenta si sigue pendiente: enviada, token inválido o fallida no se vuelven a mandar.</summary>
    public bool PorEnviar => Estado == EstadoNotificacionPush.Pendiente;

    public void MarcarEnviada(DateTime ahoraUtc) => Cerrar(EstadoNotificacionPush.Enviada, null, ahoraUtc);

    /// <summary>El proveedor dijo que el token ya no existe (app desinstalada o token renovado): no se reintenta.</summary>
    public void MarcarTokenInvalido(string error, DateTime ahoraUtc) => Cerrar(EstadoNotificacionPush.TokenInvalido, error, ahoraUtc);

    /// <summary>Error transitorio. Devuelve true si quedan reintentos; si no, queda fallida.</summary>
    public bool RegistrarFallo(string error, int maximoReintentos, DateTime ahoraUtc)
    {
        if (!PorEnviar)
            throw new InvalidOperationException("La notificación ya está cerrada.");

        Intentos++;
        UltimoError = Recortar(error);
        ActualizadaEn = ahoraUtc;
        if (Intentos > maximoReintentos)
            Estado = EstadoNotificacionPush.Fallida;
        return PorEnviar;
    }

    private void Cerrar(EstadoNotificacionPush estado, string? error, DateTime ahoraUtc)
    {
        Intentos++;
        Estado = estado;
        UltimoError = error is null ? null : Recortar(error);
        ActualizadaEn = ahoraUtc;
    }

    private static string Recortar(string texto) => texto.Length <= 500 ? texto : texto[..500];
}
