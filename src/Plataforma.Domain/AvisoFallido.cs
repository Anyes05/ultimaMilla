namespace Plataforma.Domain;

public class AvisoFallido
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string Payload { get; private set; } = string.Empty;
    public string Error { get; private set; } = string.Empty;
    public int Intentos { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }
    public DateTimeOffset ActualizadoEn { get; private set; }
    public DateTimeOffset? ResueltoEn { get; private set; }

    private AvisoFallido()
    {
    }

    public static AvisoFallido Registrar(Guid eventId, Guid operadorId, Guid envioId, string payload, string error, int intentos)
    {
        if (eventId == Guid.Empty || operadorId == Guid.Empty || envioId == Guid.Empty)
            throw new ArgumentException("El evento, operador y envío son requeridos.");

        var ahora = DateTimeOffset.UtcNow;
        return new AvisoFallido
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            OperadorId = operadorId,
            EnvioId = envioId,
            Payload = payload,
            Error = error,
            Intentos = intentos,
            CreadoEn = ahora,
            ActualizadoEn = ahora
        };
    }

    public void Actualizar(string error, int intentos)
    {
        Error = error;
        Intentos = intentos;
        ActualizadoEn = DateTimeOffset.UtcNow;
        ResueltoEn = null;
    }

    public void MarcarResuelto()
    {
        ResueltoEn = DateTimeOffset.UtcNow;
        ActualizadoEn = ResueltoEn.Value;
    }
}