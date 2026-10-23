using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class RepartidoresYSeguimientoTests
{
    [Fact]
    public async Task ListarRepartidoresActivos_SoloDelOperadorAutenticado()
    {
        var db = NombreDb();
        await using (var seed = CrearContexto(db, null))
        {
            seed.Repartidores.AddRange(
                Repartidor.Registrar(
                    DatosSemilla.RepartidorNorteId,
                    DatosSemilla.OperadorNorteId,
                    "Alvaro Norte",
                    "DOC-N",
                    "0991",
                    DatosSemilla.UsuarioRepartidorNorteId),
                Repartidor.Registrar(
                    DatosSemilla.RepartidorSurId,
                    DatosSemilla.OperadorSurId,
                    "Repartidor Sur",
                    "DOC-S",
                    "0992",
                    DatosSemilla.UsuarioRepartidorSurId));
            await seed.SaveChangesAsync();
        }

        await using var norte = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var lista = await new ListarRepartidoresActivos(new RepartidorRepositorio(norte))
            .EjecutarAsync(DatosSemilla.OperadorNorteId);

        var visible = Assert.Single(lista);
        Assert.Equal(DatosSemilla.RepartidorNorteId, visible.Id);
        Assert.Equal("Alvaro Norte", visible.Nombre);
        Assert.True(visible.Activo);
    }

    [Fact]
    public async Task CambiarEstadoEnvio_ActualizaSeguimientoEnCacheAlInstante()
    {
        var db = NombreDb();
        var cache = new CacheLecturaMemoria();
        Guid envioId;
        string token;

        await using (var seed = CrearContexto(db, null))
        {
            var envio = Envio.Admitir(
                DatosSemilla.OperadorNorteId,
                DatosSemilla.ComercioNorteId,
                "Destinatario",
                "Calle 1");
            envioId = envio.Id;
            token = envio.TokenSeguimientoPublico;
            seed.Envios.Add(envio);
            await seed.SaveChangesAsync();
        }

        await using var contexto = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var outbox = new OutboxRepositorio(contexto);
        var casoUso = new CambiarEstadoEnvio(
            new EnvioRepositorio(contexto),
            new RegistrarOutboxEstado(outbox, new HistorialEstadoRepositorio(contexto)),
            new PublicarOutboxPendientes(outbox, new PublicadorMensajesPrueba { Disponible = false }),
            cache);

        await casoUso.EjecutarAsync(envioId, DatosSemilla.OperadorNorteId, EstadoEnvio.EnDeposito);

        var cacheado = await cache.LeerAsync(ClavesCache.Seguimiento(token));
        Assert.NotNull(cacheado);
        Assert.Contains("EnDeposito", cacheado);

        var visible = await new ObtenerSeguimientoEnvio(new EnvioRepositorio(contexto), cache)
            .PorTokenAsync(token);
        Assert.Equal(EstadoEnvio.EnDeposito, visible!.Estado);
        Assert.True(cache.Estadisticas().Hits >= 1);
    }

    private static string NombreDb() => $"repartidores-{Guid.NewGuid():N}";

    private static PlataformaDbContext CrearContexto(string nombreDb, Guid? operadorId)
    {
        var operador = new OperadorActualFijo(operadorId);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase(nombreDb)
            .AddInterceptors(new OperadorSaveChangesInterceptor(operador))
            .Options;
        return new PlataformaDbContext(options, operador);
    }
}
