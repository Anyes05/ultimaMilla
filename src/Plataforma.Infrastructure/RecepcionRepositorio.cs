using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public sealed class RecepcionRepositorio : IRecepcionRepositorio
{
    private readonly PlataformaDbContext _context;
    public RecepcionRepositorio(PlataformaDbContext context) => _context = context;

    public async Task AgregarAsync(RecepcionEnvio recepcion, CancellationToken cancellationToken = default) =>
        await _context.RecepcionesEnvio.AddAsync(recepcion, cancellationToken);

    public async Task<IReadOnlyList<DiscrepanciaRecepcion>> ListarDiscrepanciasAsync(
        Guid operadorId,
        int limite,
        CancellationToken cancellationToken = default) =>
        await (from recepcion in _context.RecepcionesEnvio.AsNoTracking()
               join envio in _context.Envios.AsNoTracking() on recepcion.EnvioId equals envio.Id
               where recepcion.OperadorId == operadorId && recepcion.HayDiscrepancia
               orderby recepcion.Fecha descending
               select new DiscrepanciaRecepcion(
                   envio.NumeroSeguimiento,
                   envio.Destinatario,
                   recepcion.BultosEsperados,
                   recepcion.BultosRecibidos,
                   recepcion.Observaciones,
                   recepcion.Usuario,
                   recepcion.Fecha,
                   recepcion.BultosFaltantes,
                   recepcion.BultosRecibidos >= recepcion.BultosEsperados))
            .Take(limite)
            .ToListAsync(cancellationToken);

    public async Task AgregarSobranteAsync(BultoSobrante sobrante, CancellationToken cancellationToken = default) =>
        await _context.BultosSobrantes.AddAsync(sobrante, cancellationToken);

    public async Task<IReadOnlyList<SobranteRecepcion>> ListarSobrantesAsync(
        Guid operadorId,
        int limite,
        CancellationToken cancellationToken = default) =>
        await (from sobrante in _context.BultosSobrantes.AsNoTracking()
               join envio in _context.Envios.AsNoTracking() on sobrante.EnvioId equals envio.Id into envios
               from envio in envios.DefaultIfEmpty()
               where sobrante.OperadorId == operadorId
               orderby sobrante.Fecha descending
               select new SobranteRecepcion(
                   sobrante.Codigo,
                   envio == null ? null : envio.NumeroSeguimiento,
                   envio == null ? null : envio.Destinatario,
                   sobrante.Motivo,
                   sobrante.Usuario,
                   sobrante.Fecha))
            .Take(limite)
            .ToListAsync(cancellationToken);

    public Task<RecepcionEnvio?> ObtenerUltimaAsync(Guid operadorId, Guid envioId, CancellationToken cancellationToken = default) =>
        _context.RecepcionesEnvio
            .Where(recepcion => recepcion.OperadorId == operadorId && recepcion.EnvioId == envioId)
            .OrderByDescending(recepcion => recepcion.Fecha)
            .FirstOrDefaultAsync(cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
