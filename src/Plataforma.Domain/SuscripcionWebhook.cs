namespace Plataforma.Domain;

public class SuscripcionWebhook
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public bool Activa { get; private set; }

    private SuscripcionWebhook()
    {
    }

    public static SuscripcionWebhook Registrar(Guid id, Guid operadorId, Guid comercioId, string url)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La suscripción necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La suscripción necesita un operador.");

        if (comercioId == Guid.Empty)
            throw new ArgumentException("La suscripción necesita un comercio.");

        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("La URL es requerida.");

        return new SuscripcionWebhook
        {
            Id = id,
            OperadorId = operadorId,
            ComercioId = comercioId,
            Url = url.Trim(),
            Activa = true
        };
    }
}
