using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public class EnvioRepositorio : IEnvioRepositorio
{
    private readonly PlataformaDbContext _context;

    public EnvioRepositorio(PlataformaDbContext context)
    {
        _context = context;
    }

    public async Task AgregarAsync(Envio envio)
    {
        await _context.Envios.AddAsync(envio);
        await _context.SaveChangesAsync();
    }

    public async Task<List<Envio>> ObtenerTodosAsync()
    {
        return await _context.Envios.ToListAsync();
    }

    public Task<List<Envio>> ListarAsync(Guid operadorId, Guid? comercioId, CancellationToken cancellationToken = default)
    {
        var consulta = _context.Envios.AsNoTracking().Where(envio => envio.OperadorId == operadorId);
        if (comercioId is Guid comercio)
            consulta = consulta.Where(envio => envio.ComercioId == comercio);

        return consulta.OrderByDescending(envio => envio.FechaCreacion).ToListAsync(cancellationToken);
    }

    public Task<List<Envio>> ObtenerPorRutaAsync(Guid operadorId, Guid rutaId, CancellationToken cancellationToken = default) =>
        _context.Envios.AsNoTracking()
            .Where(envio => envio.OperadorId == operadorId && envio.RutaId == rutaId)
            .ToListAsync(cancellationToken);

    public Task<Envio?> ObtenerEnRutaAsync(
        Guid envioId,
        Guid operadorId,
        IReadOnlyCollection<Guid> rutasId,
        CancellationToken cancellationToken = default) =>
        _context.Envios.FirstOrDefaultAsync(
            envio => envio.Id == envioId && envio.OperadorId == operadorId && envio.RutaId != null && rutasId.Contains(envio.RutaId.Value),
            cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}