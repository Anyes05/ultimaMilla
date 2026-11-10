namespace Plataforma.Domain;

/// <summary>Estado actual de una bandera de funcionalidad por operador (opcional 7.4).</summary>
public class BanderaOperador
{
    public Guid OperadorId { get; private set; }
    public string Clave { get; private set; } = string.Empty;
    public bool Activa { get; private set; }

    private BanderaOperador()
    {
    }

    public static BanderaOperador Crear(Guid operadorId, string clave, bool activa)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));
        if (string.IsNullOrWhiteSpace(clave))
            throw new ArgumentException("La clave de bandera es requerida.", nameof(clave));

        return new BanderaOperador
        {
            OperadorId = operadorId,
            Clave = clave.Trim(),
            Activa = activa
        };
    }

    public void Cambiar(bool activa) => Activa = activa;
}

/// <summary>Auditoría inmutable de cambios de bandera.</summary>
public class CambioBandera
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public string Clave { get; private set; } = string.Empty;
    public bool Activa { get; private set; }
    public string Usuario { get; private set; } = string.Empty;
    public DateTimeOffset Fecha { get; private set; }

    private CambioBandera()
    {
    }

    public static CambioBandera Registrar(
        Guid id,
        Guid operadorId,
        string clave,
        bool activa,
        string usuario,
        DateTimeOffset? fecha = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("El cambio necesita un identificador.", nameof(id));
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));
        if (string.IsNullOrWhiteSpace(clave))
            throw new ArgumentException("La clave es requerida.", nameof(clave));
        if (string.IsNullOrWhiteSpace(usuario))
            throw new ArgumentException("El usuario es requerido.", nameof(usuario));

        return new CambioBandera
        {
            Id = id,
            OperadorId = operadorId,
            Clave = clave.Trim(),
            Activa = activa,
            Usuario = usuario.Trim(),
            Fecha = fecha ?? DateTimeOffset.UtcNow
        };
    }
}
