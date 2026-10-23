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
}
