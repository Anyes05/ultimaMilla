namespace Plataforma.Domain;

/// <summary>
/// Evidencia que registra la app del repartidor al ir a un retiro (RF 40): los códigos de los bultos que se llevó,
/// una foto de su estado y la firma de quien los entrega, o el motivo si no se pudo retirar. Es inmutable y el
/// <see cref="EventoId"/> del teléfono la hace idempotente: reenviarla no la duplica.
/// </summary>
public class ComprobanteRetiro
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid RetiroId { get; private set; }
    public Guid EventoId { get; private set; }

    /// <summary>Completado (retirado) o NoRetirado.</summary>
    public EstadoRetiro Resultado { get; private set; }

    /// <summary>Códigos de los bultos escaneados o tipeados, uno por línea.</summary>
    public string CodigosBultos { get; private set; } = string.Empty;
    public string Foto { get; private set; } = string.Empty;
    public string Firma { get; private set; } = string.Empty;
    public string NombreEntrega { get; private set; } = string.Empty;
    public string Motivo { get; private set; } = string.Empty;
    public DateTimeOffset FechaDispositivo { get; private set; }
    public decimal? Latitud { get; private set; }
    public decimal? Longitud { get; private set; }
    public string Usuario { get; private set; } = string.Empty;
    public DateTime RegistradoUtc { get; private set; }

    public IReadOnlyList<string> Codigos =>
        CodigosBultos.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private ComprobanteRetiro()
    {
    }

    public static ComprobanteRetiro Registrar(
        Guid operadorId,
        Guid retiroId,
        Guid eventoId,
        EstadoRetiro resultado,
        IReadOnlyList<string> codigosBultos,
        string? foto,
        string? firma,
        string? nombreEntrega,
        string? motivo,
        DateTimeOffset fechaDispositivo,
        decimal? latitud,
        decimal? longitud,
        string usuario,
        DateTime registradoUtc)
    {
        if (operadorId == Guid.Empty || retiroId == Guid.Empty || eventoId == Guid.Empty)
            throw new ArgumentException("El comprobante del retiro necesita operador, retiro y evento.");
        if (resultado is not (EstadoRetiro.Completado or EstadoRetiro.NoRetirado))
            throw new ArgumentException("El resultado del retiro es Retirado o NoRetirado.", nameof(resultado));
        if (resultado == EstadoRetiro.Completado)
        {
            if (codigosBultos.Count == 0)
                throw new ArgumentException("El retiro necesita la identificación de al menos un bulto.", nameof(codigosBultos));
            if (string.IsNullOrWhiteSpace(foto))
                throw new ArgumentException("El retiro necesita una foto del estado de los bultos.", nameof(foto));
            if (string.IsNullOrWhiteSpace(firma))
                throw new ArgumentException("El retiro necesita la firma de quien entrega los bultos.", nameof(firma));
        }
        else if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("Indicá por qué no se retiró.", nameof(motivo));
        }

        return new ComprobanteRetiro
        {
            Id = Guid.NewGuid(),
            OperadorId = operadorId,
            RetiroId = retiroId,
            EventoId = eventoId,
            Resultado = resultado,
            CodigosBultos = string.Join('\n', codigosBultos.Select(codigo => codigo.Trim())),
            Foto = foto ?? string.Empty,
            Firma = firma ?? string.Empty,
            NombreEntrega = (nombreEntrega ?? string.Empty).Trim(),
            Motivo = (motivo ?? string.Empty).Trim(),
            FechaDispositivo = fechaDispositivo.ToUniversalTime(),
            Latitud = latitud,
            Longitud = longitud,
            Usuario = usuario,
            RegistradoUtc = registradoUtc
        };
    }
}
