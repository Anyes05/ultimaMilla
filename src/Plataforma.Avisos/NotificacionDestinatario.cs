namespace Plataforma.Avisos;

public enum EstadoNotificacionDestinatario
{
    Pendiente,
    Enviada,
    Fallida
}

/// <summary>
/// Aviso al destinatario por un cambio de estado de su envío (RF 27). Es el registro de idempotencia: EventId es único,
/// así que el mismo evento repetido no genera un segundo correo (letra 6.8). No guarda la dirección de correo:
/// se lee del envío al momento de mandarlo.
/// </summary>
public sealed class NotificacionDestinatario
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string EstadoEnvio { get; private set; } = string.Empty;

    /// <summary>Mensaje original, para reintentar exactamente el mismo evento.</summary>
    public string Evento { get; private set; } = string.Empty;

    public EstadoNotificacionDestinatario Estado { get; private set; }
    public int Intentos { get; private set; }
    public string? UltimoError { get; private set; }
    public DateTime CreadaEn { get; private set; }
    public DateTime ActualizadaEn { get; private set; }

    private NotificacionDestinatario()
    {
    }

    public static NotificacionDestinatario Crear(EventoEstadoEnvio evento, string eventoJson, DateTime ahoraUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventId = evento.EventId,
            OperadorId = evento.OperadorId,
            EnvioId = evento.EnvioId,
            EstadoEnvio = evento.EstadoNuevo,
            Evento = eventoJson,
            Estado = EstadoNotificacionDestinatario.Pendiente,
            CreadaEn = ahoraUtc,
            ActualizadaEn = ahoraUtc
        };

    /// <summary>Solo se intenta si sigue pendiente: una enviada o fallida no se vuelve a mandar sola.</summary>
    public bool PorEnviar => Estado == EstadoNotificacionDestinatario.Pendiente;

    public void MarcarEnviada(DateTime ahoraUtc)
    {
        Intentos++;
        Estado = EstadoNotificacionDestinatario.Enviada;
        UltimoError = null;
        ActualizadaEn = ahoraUtc;
    }

    /// <summary>Error al enviar. Devuelve true si quedan reintentos; si no, queda fallida.</summary>
    public bool RegistrarFallo(string error, int maximoReintentos, DateTime ahoraUtc)
    {
        if (!PorEnviar)
            throw new InvalidOperationException("La notificación ya está cerrada.");

        Intentos++;
        UltimoError = error.Length <= 500 ? error : error[..500];
        ActualizadaEn = ahoraUtc;
        if (Intentos > maximoReintentos)
            Estado = EstadoNotificacionDestinatario.Fallida;
        return PorEnviar;
    }

    /// <summary>Reintento manual del administrador (letra 6.8).</summary>
    public void ReiniciarParaReintento(DateTime ahoraUtc)
    {
        if (Estado != EstadoNotificacionDestinatario.Fallida)
            throw new InvalidOperationException("Solo se reintentan las notificaciones fallidas.");

        Estado = EstadoNotificacionDestinatario.Pendiente;
        Intentos = 0;
        ActualizadaEn = ahoraUtc;
    }

    /// <summary>La cola no aceptó el reintento: vuelve a la lista de fallidas para poder intentarlo de nuevo.</summary>
    public void VolverAFallida(string error, DateTime ahoraUtc)
    {
        Estado = EstadoNotificacionDestinatario.Fallida;
        UltimoError = error;
        ActualizadaEn = ahoraUtc;
    }
}
