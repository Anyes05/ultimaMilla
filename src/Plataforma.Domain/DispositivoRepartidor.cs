namespace Plataforma.Domain;

/// <summary>Token FCM/APNs del móvil del repartidor (avisos push).</summary>
public class DispositivoRepartidor
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid RepartidorId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public string Plataforma { get; private set; } = string.Empty;
    public DateTimeOffset RegistradoEn { get; private set; }

    private DispositivoRepartidor()
    {
    }

    public static DispositivoRepartidor Registrar(
        Guid id,
        Guid operadorId,
        Guid repartidorId,
        string token,
        string plataforma,
        DateTimeOffset? registradoEn = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El dispositivo necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El dispositivo necesita un operador.");

        if (repartidorId == Guid.Empty)
            throw new ArgumentException("El dispositivo necesita un repartidor.");

        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("El token del dispositivo es requerido.");

        var plat = (plataforma ?? string.Empty).Trim().ToLowerInvariant();
        if (plat is not ("android" or "ios" or "web"))
            throw new ArgumentException("La plataforma debe ser android, ios o web.");

        return new DispositivoRepartidor
        {
            Id = id,
            OperadorId = operadorId,
            RepartidorId = repartidorId,
            Token = token.Trim(),
            Plataforma = plat,
            RegistradoEn = registradoEn ?? DateTimeOffset.UtcNow
        };
    }
}
