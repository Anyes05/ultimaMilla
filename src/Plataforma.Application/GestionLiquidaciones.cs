using Plataforma.Domain;

namespace Plataforma.Application;

public interface ILiquidacionRepositorio
{
    Task AgregarAsync(Liquidacion liquidacion, CancellationToken cancellationToken = default);
    Task<Liquidacion?> ObtenerAsync(Guid operadorId, Guid liquidacionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Liquidacion>> ListarAsync(
        Guid operadorId,
        Guid? comercioId = null,
        CancellationToken cancellationToken = default);
    Task GuardarAsync(CancellationToken cancellationToken = default);
}

public sealed record ResumenReportes(
    int TotalEnvios,
    int Entregados,
    int Devueltos,
    int EnCurso,
    decimal MontoTarifasEntregadas,
    IReadOnlyDictionary<string, int> PorEstado);

public sealed class GestionLiquidaciones
{
    private readonly ILiquidacionRepositorio _liquidaciones;
    private readonly IEnvioRepositorio _envios;
    private readonly IRetiroRepositorio? _retiros;

    public GestionLiquidaciones(ILiquidacionRepositorio liquidaciones, IEnvioRepositorio envios, IRetiroRepositorio? retiros = null)
    {
        _retiros = retiros;
        _liquidaciones = liquidaciones;
        _envios = envios;
    }

    public Task<IReadOnlyList<Liquidacion>> ListarAsync(
        Guid operadorId,
        Guid? comercioId = null,
        CancellationToken cancellationToken = default) =>
        _liquidaciones.ListarAsync(operadorId, comercioId, cancellationToken);

    public async Task<Liquidacion> AbrirAsync(
        Guid operadorId,
        Guid comercioId,
        DateTime periodoDesde,
        DateTime periodoHasta,
        CancellationToken cancellationToken = default)
    {
        var envios = await _envios.ListarAsync(operadorId, comercioId, cancellationToken);
        var monto = envios
            .Where(e => e.Estado == EstadoEnvio.Entregado
                        && e.FechaCreacion >= periodoDesde
                        && e.FechaCreacion <= periodoHasta)
            .Sum(e => e.TarifaCalculada);

        // RF 41: los retiros completados pedidos en el período también se liquidan.
        if (_retiros is not null)
            monto += (await _retiros.ListarAsync(operadorId, comercioId, cancellationToken))
                .Where(r => r.Estado == EstadoRetiro.Completado
                            && r.CreadoEn.UtcDateTime >= periodoDesde
                            && r.CreadoEn.UtcDateTime <= periodoHasta)
                .Sum(r => r.TarifaCalculada);

        var liquidacion = Liquidacion.Abrir(
            Guid.NewGuid(),
            operadorId,
            comercioId,
            periodoDesde,
            periodoHasta,
            monto);
        await _liquidaciones.AgregarAsync(liquidacion, cancellationToken);
        await _liquidaciones.GuardarAsync(cancellationToken);
        return liquidacion;
    }

    public async Task CerrarAsync(
        Guid operadorId,
        Guid liquidacionId,
        CancellationToken cancellationToken = default)
    {
        var liquidacion = await _liquidaciones.ObtenerAsync(operadorId, liquidacionId, cancellationToken)
            ?? throw new InvalidOperationException("La liquidación no existe para ese operador.");
        liquidacion.Cerrar();
        await _liquidaciones.GuardarAsync(cancellationToken);
    }

    public async Task<ResumenReportes> ResumenAsync(
        Guid operadorId,
        DateTime? desde = null,
        DateTime? hasta = null,
        Guid? comercioId = null,
        CancellationToken cancellationToken = default)
    {
        var envios = await _envios.ListarAsync(operadorId, comercioId, cancellationToken);
        if (desde is DateTime d)
            envios = envios.Where(e => e.FechaCreacion >= d).ToList();
        if (hasta is DateTime h)
            envios = envios.Where(e => e.FechaCreacion <= h).ToList();

        var porEstado = envios
            .GroupBy(e => e.Estado.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var entregados = envios.Count(e => e.Estado == EstadoEnvio.Entregado);
        var devueltos = envios.Count(e => e.Estado == EstadoEnvio.Devuelto);
        var enCurso = envios.Count - entregados - devueltos
                      - envios.Count(e => e.Estado == EstadoEnvio.Extraviado);

        return new ResumenReportes(
            envios.Count,
            entregados,
            devueltos,
            Math.Max(0, enCurso),
            envios.Where(e => e.Estado == EstadoEnvio.Entregado).Sum(e => e.TarifaCalculada),
            porEstado);
    }
}
