namespace Plataforma.Domain;

/// <summary>Registro de recepción en depósito con posible discrepancia de bultos (RF 10).</summary>
public class RecepcionEnvio
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public int BultosEsperados { get; private set; }
    public int BultosRecibidos { get; private set; }
    public bool HayDiscrepancia { get; private set; }
    public string Observaciones { get; private set; } = string.Empty;

    /// <summary>Códigos de los bultos declarados que no se escanearon al recibir (uno por línea).</summary>
    public string BultosFaltantes { get; private set; } = string.Empty;
    public string Usuario { get; private set; } = string.Empty;
    public DateTimeOffset Fecha { get; private set; }

    /// <summary>Llegaron todos los bultos (al recibir o después): la discrepancia queda registrada pero cerrada.</summary>
    public bool Completada => BultosRecibidos >= BultosEsperados;

    private RecepcionEnvio()
    {
    }

    /// <summary>
    /// Un bulto que faltaba llegó después (RF 10): deja de figurar como faltante, suma al conteo y queda anotado
    /// cuándo llegó. Si la recepción guardó los códigos faltantes, solo acepta esos.
    /// </summary>
    public bool RegistrarLlegadaPosterior(string codigo, DateTimeOffset cuando)
    {
        if (Completada)
            return false;
        var faltantes = BultosFaltantes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (faltantes.Count > 0)
        {
            var indice = faltantes.FindIndex(faltante => string.Equals(faltante, codigo.Trim(), StringComparison.OrdinalIgnoreCase));
            if (indice < 0)
                return false;
            faltantes.RemoveAt(indice);
            BultosFaltantes = string.Join('\n', faltantes);
        }

        BultosRecibidos++;
        var nota = $"Llegó después: {codigo.Trim()} ({RelojOperacionTexto(cuando)}).";
        Observaciones = (string.IsNullOrWhiteSpace(Observaciones) ? nota : $"{Observaciones} {nota}");
        if (Observaciones.Length > 2000)
            Observaciones = Observaciones[..2000];
        return true;
    }

    private static string RelojOperacionTexto(DateTimeOffset cuando) =>
        cuando.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM HH:mm");

    public static RecepcionEnvio Registrar(
        Guid id,
        Guid operadorId,
        Guid envioId,
        int bultosEsperados,
        int bultosRecibidos,
        string usuario,
        string observaciones = "",
        IReadOnlyList<string>? bultosFaltantes = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("La recepción necesita un identificador.", nameof(id));
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));
        if (envioId == Guid.Empty)
            throw new ArgumentException("El envío es requerido.", nameof(envioId));
        if (bultosEsperados < 1)
            throw new ArgumentOutOfRangeException(nameof(bultosEsperados));
        if (bultosRecibidos < 0)
            throw new ArgumentOutOfRangeException(nameof(bultosRecibidos));
        if (string.IsNullOrWhiteSpace(usuario))
            throw new ArgumentException("El usuario es requerido.", nameof(usuario));

        var discrepancia = bultosEsperados != bultosRecibidos;
        if (discrepancia && string.IsNullOrWhiteSpace(observaciones))
            throw new ArgumentException("La discrepancia de bultos exige una observación.", nameof(observaciones));

        return new RecepcionEnvio
        {
            Id = id,
            OperadorId = operadorId,
            EnvioId = envioId,
            BultosEsperados = bultosEsperados,
            BultosRecibidos = bultosRecibidos,
            HayDiscrepancia = discrepancia,
            Observaciones = (observaciones ?? string.Empty).Trim(),
            BultosFaltantes = string.Join('\n', (bultosFaltantes ?? []).Select(codigo => codigo.Trim())),
            Usuario = usuario.Trim(),
            Fecha = DateTimeOffset.UtcNow
        };
    }
}