namespace Plataforma.Avisos;

public enum EstadoEntrega
{
    Pendiente,
    Entregado,
    Fallido
}

/// <summary>
/// Una entrega de un evento a una suscripción. Es el registro de idempotencia:
/// (EventId, SuscripcionId) es único, así que un mismo evento nunca genera dos avisos al mismo comercio.
/// </summary>
public sealed class EntregaAviso
{
    /// <summary>Tiempo tras el cual se considera abandonado un envío en curso (worker caído a mitad).</summary>
    public static readonly TimeSpan TomaVencida = TimeSpan.FromMinutes(2);

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid SuscripcionId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string TipoEvento { get; private set; } = string.Empty;

    /// <summary>Cuerpo JSON exacto que se envía (y se firma) al comercio.</summary>
    public string Payload { get; private set; } = string.Empty;

    public EstadoEntrega Estado { get; private set; }
    public int Intentos { get; private set; }
    public int? UltimoCodigoHttp { get; private set; }
    public string? UltimoError { get; private set; }
    public DateTime CreadaEn { get; private set; }
    public DateTime ActualizadaEn { get; private set; }
    public DateTime? EntregadaEn { get; private set; }

    /// <summary>Marca de "estoy enviando": evita que dos copias del mismo mensaje llamen dos veces al comercio.</summary>
    public DateTime? EnCursoDesde { get; private set; }

    public uint Version { get; private set; }

    private EntregaAviso()
    {
    }

    public static EntregaAviso Crear(
        Guid eventId,
        SuscripcionAviso suscripcion,
        Guid envioId,
        string tipoEvento,
        string payload,
        DateTime ahoraUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            SuscripcionId = suscripcion.Id,
            OperadorId = suscripcion.OperadorId,
            ComercioId = suscripcion.ComercioId,
            EnvioId = envioId,
            TipoEvento = tipoEvento,
            Payload = payload,
            Estado = EstadoEntrega.Pendiente,
            CreadaEn = ahoraUtc,
            ActualizadaEn = ahoraUtc
        };

    /// <summary>Puede tomarse para enviar: pendiente y sin otro envío en curso (o con uno abandonado).</summary>
    public bool PuedeTomarse(DateTime ahoraUtc) =>
        Estado == EstadoEntrega.Pendiente &&
        (EnCursoDesde is null || ahoraUtc - EnCursoDesde.Value > TomaVencida);

    public void Tomar(DateTime ahoraUtc)
    {
        if (!PuedeTomarse(ahoraUtc))
            throw new InvalidOperationException("La entrega no está disponible para enviarse.");

        EnCursoDesde = ahoraUtc;
        ActualizadaEn = ahoraUtc;
    }

    public void MarcarEntregada(int codigoHttp, DateTime ahoraUtc)
    {
        Intentos++;
        Estado = EstadoEntrega.Entregado;
        UltimoCodigoHttp = codigoHttp;
        UltimoError = null;
        EntregadaEn = ahoraUtc;
        EnCursoDesde = null;
        ActualizadaEn = ahoraUtc;
    }

    /// <summary>
    /// Registra un intento fallido. Devuelve true si todavía quedan reintentos automáticos;
    /// si se agotaron, la entrega queda Fallida (va a la cola de fallidos).
    /// </summary>
    public bool RegistrarFallo(string error, int? codigoHttp, int maximoReintentos, DateTime ahoraUtc)
    {
        Intentos++;
        UltimoError = error.Length > 500 ? error[..500] : error;
        UltimoCodigoHttp = codigoHttp;
        EnCursoDesde = null;
        ActualizadaEn = ahoraUtc;

        // Intentos = 1 es el envío original; los siguientes son reintentos.
        if (Intentos > maximoReintentos)
        {
            Estado = EstadoEntrega.Fallido;
            return false;
        }

        return true;
    }

    /// <summary>Reintento manual desde el Backoffice o el Portal: vuelve a Pendiente con el mismo EventId.</summary>
    public void ReiniciarParaReintento(DateTime ahoraUtc)
    {
        if (Estado != EstadoEntrega.Fallido)
            throw new InvalidOperationException("Solo se pueden reintentar avisos fallidos.");

        Estado = EstadoEntrega.Pendiente;
        Intentos = 0;
        EnCursoDesde = null;
        ActualizadaEn = ahoraUtc;
    }
}
