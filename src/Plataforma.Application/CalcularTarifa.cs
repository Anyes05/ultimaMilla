using Plataforma.Domain;

namespace Plataforma.Application;

public sealed record ResultadoTarifa(
    decimal Importe,
    Guid ReglaTarifariaId,
    Guid? VigenciaTarifaId,
    decimal RecargoZona = 0,
    decimal Descuento = 0,
    string? Bonificacion = null);

/// <summary>Calcula la tarifa con la regla activa del operador en el momento de admisión.</summary>
public sealed class CalcularTarifa
{
    private readonly ITarifaRepositorio _tarifas;
    private readonly IEnvioRepositorio? _envios;

    public CalcularTarifa(ITarifaRepositorio tarifas, IEnvioRepositorio? envios = null)
    {
        _tarifas = tarifas;
        _envios = envios;
    }

    public async Task<decimal> EjecutarAsync(
        Guid operadorId,
        decimal pesoKg = 0,
        decimal distanciaKm = 0,
        bool fragil = false,
        bool urgente = false,
        CancellationToken cancellationToken = default,
        decimal volumenCm3 = 0,
        bool aplicaZona = false)
    {
        var resultado = await EjecutarConReferenciaAsync(
            operadorId, pesoKg, distanciaKm, fragil, urgente, cancellationToken, volumenCm3, aplicaZona);
        return resultado.Importe;
    }

    /// <summary>Número que tendría este envío entre los del comercio en el mes (hora del operador, UTC−03:00).</summary>
    private async Task<int> NumeroDeEnvioEnElMesAsync(
        Guid operadorId,
        Guid? comercioId,
        IReadOnlyList<BonificacionTarifa> bonificaciones,
        CancellationToken cancellationToken)
    {
        if (comercioId is not Guid comercio || _envios is null || bonificaciones.All(b => b.MinimoEnviosMes is null))
            return 1;

        var ahora = DateTimeOffset.UtcNow.ToOffset(RelojOperacion.Offset);
        var inicioDelMes = new DateTimeOffset(ahora.Year, ahora.Month, 1, 0, 0, 0, RelojOperacion.Offset).UtcDateTime;
        return await _envios.ContarDelComercioDesdeAsync(operadorId, comercio, inicioDelMes, cancellationToken) + 1;
    }

    public async Task<ResultadoTarifa> EjecutarConReferenciaAsync(
        Guid operadorId,
        decimal pesoKg = 0,
        decimal distanciaKm = 0,
        bool fragil = false,
        bool urgente = false,
        CancellationToken cancellationToken = default,
        decimal volumenCm3 = 0,
        bool aplicaZona = false,
        Guid? zonaId = null,
        Guid? comercioId = null)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        if (pesoKg < 0 || distanciaKm < 0 || volumenCm3 < 0)
            throw new ArgumentOutOfRangeException(nameof(pesoKg), "Peso, distancia y volumen no pueden ser negativos.");

        var momento = DateTime.UtcNow;
        var vigencia = await _tarifas.ObtenerVigenciaActivaParaEditarAsync(operadorId, momento, cancellationToken);
        var regla = await _tarifas.ObtenerActivaAsync(operadorId, momento, cancellationToken)
            ?? throw new InvalidOperationException("No hay una regla tarifaria vigente para el operador.");

        var total = regla.PrecioBase
            + (pesoKg * regla.PrecioPorKg)
            + (distanciaKm * regla.PrecioPorKm)
            + (volumenCm3 * regla.PrecioPorCm3);

        // RF 3: la zona puede tener recargo propio; si no, rige el recargo general de zona.
        decimal recargoZona = 0;
        if (zonaId is Guid zona &&
            (await _tarifas.ListarRecargosZonaAsync(operadorId, regla.Id, cancellationToken)).FirstOrDefault(r => r.ZonaId == zona) is { } propio)
            recargoZona = propio.Recargo;
        else if (aplicaZona || zonaId is not null)
            recargoZona = regla.RecargoZona;

        total += recargoZona;
        if (fragil)
            total += regla.RecargoFragil;
        if (urgente)
            total += regla.RecargoUrgente;

        // Bonificaciones: se aplica la de mayor porcentaje entre las que corresponden (no se acumulan).
        decimal descuento = 0;
        string? nombreBonificacion = null;
        var bonificaciones = await _tarifas.ListarBonificacionesAsync(operadorId, regla.Id, cancellationToken);
        if (bonificaciones.Count > 0)
        {
            var numeroEnElMes = await NumeroDeEnvioEnElMesAsync(operadorId, comercioId, bonificaciones, cancellationToken);
            var mejor = bonificaciones
                .Where(bonificacion => bonificacion.AplicaA(comercioId, numeroEnElMes))
                .OrderByDescending(bonificacion => bonificacion.Porcentaje)
                .FirstOrDefault();
            if (mejor is not null)
            {
                descuento = decimal.Round(total * mejor.Porcentaje / 100m, 2, MidpointRounding.AwayFromZero);
                nombreBonificacion = mejor.Nombre;
                total -= descuento;
            }
        }

        return new ResultadoTarifa(
            decimal.Round(total, 2, MidpointRounding.AwayFromZero),
            regla.Id,
            vigencia?.Id,
            recargoZona,
            descuento,
            nombreBonificacion);
    }
}
