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
}