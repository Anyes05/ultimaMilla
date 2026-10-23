using Plataforma.Domain;

namespace Plataforma.Application;

public sealed record NuevaVersionTarifa(
    decimal PrecioBase,
    decimal PrecioPorKg,
    decimal PrecioPorKm,
    decimal RecargoFragil,
    decimal RecargoUrgente);

public sealed record TarifaVersionVista(
    Guid Id,
    DateTime Desde,
    DateTime Hasta,
    NuevaVersionTarifa Valores,
    bool Vigente);

/// <summary>Listado e historial de versiones tarifarias; publicación de una nueva vigencia.</summary>
public sealed class GestionTarifas
{
    public static readonly DateTime VigenciaAbiertaHasta = new(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc);

    private readonly ITarifaRepositorio _tarifas;

    public GestionTarifas(ITarifaRepositorio tarifas)
    {
        _tarifas = tarifas;
    }

    public async Task<IReadOnlyList<TarifaVersionVista>> ListarVersionesAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        var ahora = DateTime.UtcNow;
        var versiones = await _tarifas.ListarVersionesAsync(operadorId, cancellationToken);
        return versiones
            .OrderByDescending(v => v.Vigencia.VigenciaDesde)
            .Select(v => new TarifaVersionVista(
                v.Regla.Id,
                v.Vigencia.VigenciaDesde,
                v.Vigencia.VigenciaHasta,
                new NuevaVersionTarifa(
                    v.Regla.PrecioBase,
                    v.Regla.PrecioPorKg,
                    v.Regla.PrecioPorKm,
                    v.Regla.RecargoFragil,
                    v.Regla.RecargoUrgente),
                v.Vigencia.VigenciaDesde <= ahora && v.Vigencia.VigenciaHasta >= ahora))
            .ToList();
    }

    public async Task<Guid> PublicarVersionAsync(
        Guid operadorId,
        NuevaVersionTarifa datos,
        DateTime vigenteDesdeUtc,
        string usuario,
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        if (string.IsNullOrWhiteSpace(usuario))
            throw new ArgumentException("El usuario es requerido.", nameof(usuario));

        if (vigenteDesdeUtc.Kind == DateTimeKind.Unspecified)
            vigenteDesdeUtc = DateTime.SpecifyKind(vigenteDesdeUtc, DateTimeKind.Utc);
        else
            vigenteDesdeUtc = vigenteDesdeUtc.ToUniversalTime();

        var existentes = await _tarifas.ListarVersionesAsync(operadorId, cancellationToken);
        foreach (var (vigencia, _) in existentes)
        {
            if (vigencia.VigenciaDesde >= vigenteDesdeUtc && vigencia.VigenciaHasta >= vigenteDesdeUtc)
                throw new InvalidOperationException(
                    "Ya hay una vigencia que empieza en o después de esa fecha. Elegí una fecha posterior.");
        }

        var abierta = await _tarifas.ObtenerVigenciaActivaParaEditarAsync(
            operadorId, vigenteDesdeUtc, cancellationToken);

        if (abierta is not null)
        {
            if (abierta.VigenciaDesde >= vigenteDesdeUtc)
                throw new InvalidOperationException(
                    "La nueva vigencia debe empezar después del inicio de la vigente.");

            // Cierra justo antes del nuevo inicio para no superponer.
            abierta.Cerrar(vigenteDesdeUtc.AddTicks(-1));
            var reglaAnterior = existentes
                .Select(v => v.Regla)
                .FirstOrDefault(regla => regla.Id == abierta.ReglaTarifariaId);
            reglaAnterior?.Desactivar();
        }

        var reglaId = Guid.NewGuid();
        var regla = ReglaTarifaria.Crear(
            reglaId,
            operadorId,
            datos.PrecioBase,
            datos.PrecioPorKg,
            datos.PrecioPorKm,
            datos.RecargoFragil,
            datos.RecargoUrgente);

        var vigenciaNueva = VigenciaTarifa.Crear(
            Guid.NewGuid(),
            operadorId,
            reglaId,
            vigenteDesdeUtc,
            VigenciaAbiertaHasta);

        await _tarifas.AgregarReglaAsync(regla, cancellationToken);
        await _tarifas.AgregarVigenciaAsync(vigenciaNueva, cancellationToken);
        await _tarifas.GuardarAsync(cancellationToken);

        return reglaId;
    }
}
