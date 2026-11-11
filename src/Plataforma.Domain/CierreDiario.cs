namespace Plataforma.Domain;

/// <summary>
/// Cierre de la jornada de un operador (letra sección 4: el worker recalcula los indicadores de cumplimiento y
/// cierra el día). Lo escribe el worker; el Backoffice solo lo lee.
/// </summary>
public class CierreDiario
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }

    /// <summary>Día en la hora del operador.</summary>
    public DateOnly Dia { get; private set; }

    public int EnviosAdmitidos { get; private set; }

    /// <summary>Envíos cuya primera entrega fue ese día.</summary>
    public int Entregados { get; private set; }

    /// <summary>De los entregados, los que tenían plazo comprometido (regla vigente al alta).</summary>
    public int EntregadosConPlazo { get; private set; }

    public int EntregadosATiempo { get; private set; }

    /// <summary>Intentos fallidos del día: no entregados y entregas parciales.</summary>
    public int IntentosFallidos { get; private set; }

    public int Devueltos { get; private set; }
    public int RetirosCompletados { get; private set; }

    /// <summary>Última vez que el worker lo calculó (se recalcula si llegan sincronizaciones atrasadas).</summary>
    public DateTime CalculadoUtc { get; private set; }

    private CierreDiario()
    {
    }

    public static CierreDiario Registrar(
        Guid operadorId,
        DateOnly dia,
        int enviosAdmitidos,
        int entregados,
        int entregadosConPlazo,
        int entregadosATiempo,
        int intentosFallidos,
        int devueltos,
        int retirosCompletados,
        DateTime calculadoUtc)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El cierre necesita un operador.", nameof(operadorId));
        if (entregadosATiempo > entregadosConPlazo || entregadosConPlazo > entregados)
            throw new ArgumentException("Los entregados a tiempo no pueden superar a los entregados con plazo, ni estos a los entregados.");

        return new CierreDiario
        {
            Id = Guid.NewGuid(),
            OperadorId = operadorId,
            Dia = dia,
            EnviosAdmitidos = enviosAdmitidos,
            Entregados = entregados,
            EntregadosConPlazo = entregadosConPlazo,
            EntregadosATiempo = entregadosATiempo,
            IntentosFallidos = intentosFallidos,
            Devueltos = devueltos,
            RetirosCompletados = retirosCompletados,
            CalculadoUtc = DateTime.SpecifyKind(calculadoUtc, DateTimeKind.Utc)
        };
    }

    /// <summary>Porcentaje de entregas a tiempo sobre las que tenían plazo; sin entregas con plazo, no hay dato.</summary>
    public decimal? PorcentajeATiempo =>
        EntregadosConPlazo == 0 ? null : Math.Round(100m * EntregadosATiempo / EntregadosConPlazo, 1);

    /// <summary>Entregas sobre intentos del día (entregas más intentos fallidos).</summary>
    public decimal? PorcentajeEfectividad =>
        Entregados + IntentosFallidos == 0 ? null : Math.Round(100m * Entregados / (Entregados + IntentosFallidos), 1);
}
