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
                   recepcion.Fecha))
            .Take(limite)
            .ToListAsync(cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
