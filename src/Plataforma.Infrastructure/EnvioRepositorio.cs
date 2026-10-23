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

    public Task<Envio?> ObtenerPorOperadorAsync(Guid envioId, Guid operadorId, CancellationToken cancellationToken = default) =>
        _context.Envios.FirstOrDefaultAsync(
            envio => envio.Id == envioId && envio.OperadorId == operadorId,
            cancellationToken);

    public Task<Envio?> ObtenerPorTokenAsync(string tokenSeguimientoPublico, CancellationToken cancellationToken = default) =>
        _context.Envios.AsNoTracking().FirstOrDefaultAsync(
            envio => envio.TokenSeguimientoPublico == tokenSeguimientoPublico,
            cancellationToken);

    public Task<Envio?> ObtenerPorNumeroAsync(string numeroSeguimiento, CancellationToken cancellationToken = default) =>
        _context.Envios.AsNoTracking().FirstOrDefaultAsync(
            envio => envio.NumeroSeguimiento == numeroSeguimiento,
            cancellationToken);

    public Task<List<Envio>> ListarAsync(Guid operadorId, Guid? comercioId, CancellationToken cancellationToken = default)
    {
        var consulta = _context.Envios.AsNoTracking().Where(envio => envio.OperadorId == operadorId);
        if (comercioId is Guid comercio)
            consulta = consulta.Where(envio => envio.ComercioId == comercio);

        return consulta.OrderByDescending(envio => envio.FechaCreacion).ToListAsync(cancellationToken);
    }

    public async Task<ResultadoPaginado<Envio>> ListarPaginadoAsync(
        Guid operadorId,
        Guid? comercioId,
        EstadoEnvio? estado,
        int pagina,
        int tamanoPagina,
        CancellationToken cancellationToken = default)
    {
        if (pagina < 1)
            pagina = 1;
        if (tamanoPagina < 1)
            tamanoPagina = 50;
        if (tamanoPagina > 200)
            tamanoPagina = 200;

        var consulta = _context.Envios.AsNoTracking().Where(envio => envio.OperadorId == operadorId);
        if (comercioId is Guid comercio)
            consulta = consulta.Where(envio => envio.ComercioId == comercio);
        if (estado is EstadoEnvio filtroEstado)
            consulta = consulta.Where(envio => envio.Estado == filtroEstado);

        var total = await consulta.CountAsync(cancellationToken);
        var items = await consulta
            .OrderByDescending(envio => envio.FechaCreacion)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .ToListAsync(cancellationToken);

        return new ResultadoPaginado<Envio>(items, total, pagina, tamanoPagina);
    }

    public Task<List<Envio>> ObtenerPorRutaAsync(Guid operadorId, Guid rutaId, CancellationToken cancellationToken = default) =>
        _context.Envios.AsNoTracking()
            .Where(envio => envio.OperadorId == operadorId && envio.RutaId == rutaId)
            .ToListAsync(cancellationToken);

    public Task<List<Envio>> ObtenerPorRutaParaEditarAsync(
        Guid operadorId,
        Guid rutaId,
        CancellationToken cancellationToken = default) =>
        _context.Envios
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
