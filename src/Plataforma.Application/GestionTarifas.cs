using Plataforma.Domain;

namespace Plataforma.Application;

public sealed record NuevaVersionTarifa(
    decimal PrecioBase,
    decimal PrecioPorKg,
    decimal PrecioPorKm,
    decimal RecargoFragil,
    decimal RecargoUrgente,
    decimal PrecioPorCm3 = 0,
    decimal RecargoZona = 0,
    IReadOnlyList<RecargoZonaDato>? RecargosZona = null,
    IReadOnlyList<BonificacionDato>? Bonificaciones = null,
    decimal PrecioRetiro = 0);

/// <summary>Recargo propio de una zona en la versión que se publica.</summary>
public sealed record RecargoZonaDato(Guid ZonaId, decimal Recargo);

/// <summary>Bonificación de la versión que se publica (porcentaje; comercio y mínimo de envíos opcionales).</summary>
public sealed record BonificacionDato(string Nombre, decimal Porcentaje, Guid? ComercioId = null, int? MinimoEnviosMes = null);

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
    private readonly ICacheLectura _cache;

    public GestionTarifas(ITarifaRepositorio tarifas, ICacheLectura cache)
    {
        _tarifas = tarifas;
        _cache = cache;
    }

    public async Task<IReadOnlyList<TarifaVersionVista>> ListarVersionesAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        var ahora = DateTime.UtcNow;
        var versiones = await _tarifas.ListarVersionesAsync(operadorId, cancellationToken);
        var vistas = new List<TarifaVersionVista>(versiones.Count);
        foreach (var v in versiones.OrderByDescending(v => v.Vigencia.VigenciaDesde))
        {
            var recargos = await _tarifas.ListarRecargosZonaAsync(operadorId, v.Regla.Id, cancellationToken);
            var bonificaciones = await _tarifas.ListarBonificacionesAsync(operadorId, v.Regla.Id, cancellationToken);
            vistas.Add(new TarifaVersionVista(
                v.Regla.Id,
                v.Vigencia.VigenciaDesde,
                v.Vigencia.VigenciaHasta,
                new NuevaVersionTarifa(
                    v.Regla.PrecioBase,
                    v.Regla.PrecioPorKg,
                    v.Regla.PrecioPorKm,
                    v.Regla.RecargoFragil,
                    v.Regla.RecargoUrgente,
                    v.Regla.PrecioPorCm3,
                    v.Regla.RecargoZona,
                    recargos.Select(r => new RecargoZonaDato(r.ZonaId, r.Recargo)).ToList(),
                    bonificaciones.Select(b => new BonificacionDato(b.Nombre, b.Porcentaje, b.ComercioId, b.MinimoEnviosMes)).ToList(),
                    v.Regla.PrecioRetiro),
                v.Vigencia.VigenciaDesde <= ahora && v.Vigencia.VigenciaHasta >= ahora));
        }

        return vistas;
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
            datos.RecargoUrgente,
            datos.PrecioPorCm3,
            datos.RecargoZona,
            datos.PrecioRetiro);

        var recargosZona = (datos.RecargosZona ?? [])
            .GroupBy(recargo => recargo.ZonaId)
            .Select(grupo => RecargoZonaTarifa.Crear(Guid.NewGuid(), operadorId, reglaId, grupo.Key, grupo.Last().Recargo))
            .ToList();
        var bonificaciones = (datos.Bonificaciones ?? [])
            .Select(b => BonificacionTarifa.Crear(Guid.NewGuid(), operadorId, reglaId, b.Nombre, b.Porcentaje, b.ComercioId, b.MinimoEnviosMes))
            .ToList();

        var vigenciaNueva = VigenciaTarifa.Crear(
            Guid.NewGuid(),
            operadorId,
            reglaId,
            vigenteDesdeUtc,
            VigenciaAbiertaHasta);

        await _tarifas.AgregarReglaAsync(regla, cancellationToken);
        await _tarifas.AgregarVigenciaAsync(vigenciaNueva, cancellationToken);
        await _tarifas.AgregarCondicionesAsync(recargosZona, bonificaciones, cancellationToken);
        await _tarifas.GuardarAsync(cancellationToken);
        // Aclaración 3.1: invalidar al publicar para que todas las réplicas vean la nueva vigencia.
        await _cache.QuitarAsync(ClavesCache.TarifaVigente(operadorId), cancellationToken);
        await _cache.QuitarAsync(ClavesCache.Operador(operadorId), cancellationToken);

        return reglaId;
    }
}
