namespace Plataforma.Domain;

public class Operador
{
    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public string ColorMarca { get; private set; } = string.Empty;
    public int MaximoIntentos { get; private set; }

    private Operador()
    {
    }

    public static Operador Crear(Guid id, string nombre, string colorMarca, int maximoIntentos)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El operador necesita un identificador.");

        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre del operador es requerido.");

        if (string.IsNullOrWhiteSpace(colorMarca))
            throw new ArgumentException("El color de marca es requerido.");

        if (maximoIntentos < 1)
            throw new ArgumentOutOfRangeException(nameof(maximoIntentos), "El máximo de intentos debe ser al menos 1.");

        return new Operador
        {
            Id = id,
            Nombre = nombre.Trim(),
            ColorMarca = colorMarca.Trim().ToLowerInvariant(),
            MaximoIntentos = maximoIntentos
        };
    }
}
