namespace Plataforma.Domain;

public class Parada
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid RutaId { get; private set; }
    public Guid EnvioId { get; private set; }
    public int Orden { get; private set; }
    public DateTime? CompletadaEn { get; private set; }

    private Parada()
    {
    }

    public static Parada Programar(Guid id, Guid operadorId, Guid rutaId, Guid envioId, int orden)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La parada necesita un identificador.");

        if (operadorId == Guid.Empty)
            throw new ArgumentException("La parada necesita un operador.");

        if (rutaId == Guid.Empty)
            throw new ArgumentException("La parada necesita una ruta.");

        if (envioId == Guid.Empty)
            throw new ArgumentException("La parada necesita un envío.");

        if (orden < 1)
            throw new ArgumentOutOfRangeException(nameof(orden), "El orden de la parada empieza en 1.");

        return new Parada
        {
            Id = id,
            OperadorId = operadorId,
            RutaId = rutaId,
            EnvioId = envioId,
            Orden = orden
        };
    }
}
