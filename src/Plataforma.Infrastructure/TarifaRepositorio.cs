using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class TarifaRepositorio : ITarifaRepositorio
{
    private readonly PlataformaDbContext _context;

    public TarifaRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public async Task<ReglaTarifaria?> ObtenerActivaAsync(
        Guid operadorId,
        DateTime momentoUtc,
        CancellationToken cancellationToken = default)
    {
        var vigencia = await _context.VigenciasTarifa.AsNoTracking()
            .Where(v =>
                v.OperadorId == operadorId &&
                v.VigenciaDesde <= momentoUtc &&
                v.VigenciaHasta >= momentoUtc)
            .OrderByDescending(v => v.VigenciaDesde)
            .FirstOrDefaultAsync(cancellationToken);

        if (vigencia is null)
        {
            return await _context.ReglasTarifarias.AsNoTracking()
                .Where(regla => regla.OperadorId == operadorId && regla.Activa)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _context.ReglasTarifarias.AsNoTracking()
            .FirstOrDefaultAsync(
                regla => regla.Id == vigencia.ReglaTarifariaId && regla.OperadorId == operadorId && regla.Activa,
                cancellationToken);
    }

    public async Task<IReadOnlyList<(VigenciaTarifa Vigencia, ReglaTarifaria Regla)>> ListarVersionesAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default)
    {
        var vigencias = await _context.VigenciasTarifa.AsNoTracking()
            .Where(v => v.OperadorId == operadorId)
            .OrderByDescending(v => v.VigenciaDesde)
            .ToListAsync(cancellationToken);

        if (vigencias.Count == 0)
            return [];

        var reglaIds = vigencias.Select(v => v.ReglaTarifariaId).Distinct().ToArray();
        var reglas = await _context.ReglasTarifarias.AsNoTracking()
            .Where(regla => regla.OperadorId == operadorId && reglaIds.Contains(regla.Id))
            .ToDictionaryAsync(regla => regla.Id, cancellationToken);

        var resultado = new List<(VigenciaTarifa, ReglaTarifaria)>(vigencias.Count);
        foreach (var vigencia in vigencias)
        {
            if (reglas.TryGetValue(vigencia.ReglaTarifariaId, out var regla))
                resultado.Add((vigencia, regla));
        }

        return resultado;
    }

    public Task<VigenciaTarifa?> ObtenerVigenciaActivaParaEditarAsync(
        Guid operadorId,
        DateTime momentoUtc,
        CancellationToken cancellationToken = default) =>
        _context.VigenciasTarifa
            .Where(v =>
                v.OperadorId == operadorId &&
                v.VigenciaDesde <= momentoUtc &&
                v.VigenciaHasta >= momentoUtc)
            .OrderByDescending(v => v.VigenciaDesde)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AgregarReglaAsync(ReglaTarifaria regla, CancellationToken cancellationToken = default) =>
        await _context.ReglasTarifarias.AddAsync(regla, cancellationToken);

    public async Task AgregarVigenciaAsync(VigenciaTarifa vigencia, CancellationToken cancellationToken = default) =>
        await _context.VigenciasTarifa.AddAsync(vigencia, cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
