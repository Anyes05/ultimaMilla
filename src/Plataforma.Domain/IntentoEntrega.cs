namespace Plataforma.Domain;

public class IntentoEntrega
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public int Numero { get; private set; }
    public DateTime Fecha { get; private set; }
    public string Resultado { get; private set; } = string.Empty;

    private IntentoEntrega()
    {
    }

    public static IntentoEntrega Registrar(
        Guid id,
        Guid operadorId,
        Guid envioId,
        int numero,
        DateTime fecha,
        string resultado)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El intento necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("El intento necesita un operador.");

        if (envioId == Guid.Empty)
            throw new ArgumentException("El intento necesita un envío.");

        if (numero < 1)
            throw new ArgumentOutOfRangeException(nameof(numero), "El número de intento empieza en 1.");

        return new IntentoEntrega
        {
            Id = id,
            OperadorId = operadorId,
            EnvioId = envioId,
            Numero = numero,
            Fecha = fecha,
            Resultado = resultado.Trim()
        };
    }
}
