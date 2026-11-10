using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class GestionRetirosTests
{
    [Fact]
    public async Task Solicitar_ConBanderaActiva_PersisteYEscribeOutbox()
    {
        await using var contexto = CrearContexto();
        var gestion = CrearGestion(contexto, banderaActiva: true);

        var retiro = await gestion.SolicitarAsync(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.ComercioNorteId,
            "Av. Italia 1234",
            "099123456",
            new DateOnly(2026, 11, 12));

        Assert.Equal(EstadoRetiro.Solicitado, retiro.Estado);
        Assert.Equal(1, await contexto.SolicitudesRetiro.CountAsync());
        Assert.Contains(await contexto.MensajesOutbox.ToListAsync(), m => m.Tipo == "RetiroSolicitado");
    }

    [Fact]
    public async Task Solicitar_SinBandera_Rechaza()
    {
        await using var contexto = CrearContexto();
        var gestion = CrearGestion(contexto, banderaActiva: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gestion.SolicitarAsync(
                DatosSemilla.OperadorNorteId,
                DatosSemilla.ComercioNorteId,
                "Av. Italia 1234",
                "099",
                new DateOnly(2026, 11, 12)));
    }

    [Fact]
    public async Task Solicitar_ComercioDadoDeBaja_Rechaza()
    {
        await using var contexto = CrearContexto();
        (await contexto.Comercios.SingleAsync()).Desactivar();
        await contexto.SaveChangesAsync();
        var gestion = CrearGestion(contexto, banderaActiva: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gestion.SolicitarAsync(
                DatosSemilla.OperadorNorteId,
                DatosSemilla.ComercioNorteId,
                "Calle 1",
                "099",
                new DateOnly(2026, 11, 12)));
        Assert.Equal(0, await contexto.SolicitudesRetiro.CountAsync());
    }

    [Fact]
    public async Task CompletarYCancelar_CambianEstado()
    {
        await using var contexto = CrearContexto();
        var gestion = CrearGestion(contexto, banderaActiva: true);
        var retiro = await gestion.SolicitarAsync(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.ComercioNorteId,
            "Calle 1",
            "099",
            new DateOnly(2026, 11, 12));

        await gestion.CompletarAsync(DatosSemilla.OperadorNorteId, retiro.Id);
        Assert.Equal(EstadoRetiro.Completado, (await contexto.SolicitudesRetiro.SingleAsync()).Estado);
        Assert.Contains(await contexto.MensajesOutbox.ToListAsync(), m => m.Tipo == "RetiroCompletado");

        var otro = await gestion.SolicitarAsync(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.ComercioNorteId,
            "Calle 2",
            "099",
            new DateOnly(2026, 11, 13));
        await gestion.CancelarAsync(DatosSemilla.OperadorNorteId, otro.Id, "Cliente canceló");
        Assert.Equal(EstadoRetiro.Cancelado,
            (await contexto.SolicitudesRetiro.SingleAsync(r => r.Id == otro.Id)).Estado);
    }

    private static GestionRetiros CrearGestion(PlataformaDbContext contexto, bool banderaActiva) =>
        new(
            new RetiroRepositorio(contexto),
            new BanderaFija(banderaActiva),
            new OutboxRepositorio(contexto),
            new PublicarOutboxPendientes(
                new OutboxRepositorio(contexto),
                new PublicadorMensajesPrueba { Disponible = false }),
            new ComercioRepositorio(contexto));

    private static PlataformaDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase($"retiros-{Guid.NewGuid():N}")
            .Options;
        var contexto = new PlataformaDbContext(options);
        contexto.Comercios.Add(Comercio.Registrar(
            DatosSemilla.ComercioNorteId, DatosSemilla.OperadorNorteId, "Comercio Norte", "Comercio Norte SA", "210000000011", "Calle 1"));
        contexto.SaveChanges();
        return contexto;
    }

    private sealed class BanderaFija(bool activa) : IBanderasOperador
    {
        public Task<bool> EstaActivaAsync(Guid operadorId, string clave, CancellationToken cancellationToken = default) =>
            Task.FromResult(activa);
    }
}
