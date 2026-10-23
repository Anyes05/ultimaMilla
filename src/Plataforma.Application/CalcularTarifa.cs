using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Calcula la tarifa con la regla activa del operador en el momento de admisión.</summary>
public sealed class CalcularTarifa
{
    private readonly ITarifaRepositorio _tarifas;

    public CalcularTarifa(ITarifaRepositorio tarifas)
    {
        _tarifas = tarifas;
    }

    public async Task<decimal> EjecutarAsync(
        Guid operadorId,
        decimal pesoKg = 0,
        decimal distanciaKm = 0,
        bool fragil = false,
        bool urgente = false,
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        if (pesoKg < 0 || distanciaKm < 0)
            throw new ArgumentOutOfRangeException(nameof(pesoKg), "Peso y distancia no pueden ser negativos.");

        var regla = await _tarifas.ObtenerActivaAsync(operadorId, DateTime.UtcNow, cancellationToken)
            ?? throw new InvalidOperationException("No hay una regla tarifaria vigente para el operador.");

        var total = regla.PrecioBase
            + (pesoKg * regla.PrecioPorKg)
            + (distanciaKm * regla.PrecioPorKm);

        if (fragil)
            total += regla.RecargoFragil;
        if (urgente)
            total += regla.RecargoUrgente;

        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }
}
