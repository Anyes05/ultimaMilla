namespace Plataforma.Domain;

/// <summary>
/// Discrepancia de recepción por un bulto de más (RF 10): se escaneó en el depósito un código que no estaba para
/// recibir, porque su envío ya se había recibido o porque no corresponde a ningún bulto pendiente.
/// </summary>
public class BultoSobrante
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public string Codigo { get; private set; } = string.Empty;

    /// <summary>Envío al que pertenece el bulto, si el código es de un bulto conocido.</summary>
    public Guid? EnvioId { get; private set; }

    public string Motivo { get; private set; } = string.Empty;
    public string Usuario { get; private set; } = string.Empty;
    public DateTimeOffset Fecha { get; private set; }

    private BultoSobrante()
    {
    }

    public static BultoSobrante Registrar(Guid operadorId, string codigo, Guid? envioId, string motivo, string usuario, DateTimeOffset fecha)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("El código escaneado es requerido.", nameof(codigo));
        if (string.IsNullOrWhiteSpace(usuario))
            throw new ArgumentException("El usuario es requerido.", nameof(usuario));

        var texto = codigo.Trim();
        return new BultoSobrante
        {
            Id = Guid.NewGuid(),
            OperadorId = operadorId,
            Codigo = texto.Length > 120 ? texto[..120] : texto,
            EnvioId = envioId,
            Motivo = motivo.Trim(),
            Usuario = usuario.Trim(),
            Fecha = fecha.ToUniversalTime()
        };
    }
}
