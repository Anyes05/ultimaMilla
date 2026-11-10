using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class RecepcionYBanderasTests
{
    [Fact]
    public async Task Recepcion_ConDiscrepancia_RegistraYPasaAEnDeposito()
    {
        var db = $"recepcion-{Guid.NewGuid():N}";
        Guid envioId;
        await using (var seed = Crear(db, null))
        {
            seed.Operadores.Add(Operador.Crear(DatosSemilla.OperadorNorteId, "Norte", "azul", 3));
            var envio = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "Dest", "Calle 1");
            envioId = envio.Id;
            seed.Envios.Add(envio);
            seed.Bultos.Add(Bulto.Crear(Guid.NewGuid(), envio.OperadorId, envio.Id, "B1", pesoKg: 1m));
            seed.Bultos.Add(Bulto.Crear(Guid.NewGuid(), envio.OperadorId, envio.Id, "B2", pesoKg: 1m));
            await seed.SaveChangesAsync();
        }

        await using var contexto = Crear(db, DatosSemilla.OperadorNorteId);
        var cache = new CacheLecturaMemoria();
        var outbox = new OutboxRepositorio(contexto);
        var historial = new HistorialEstadoRepositorio(contexto);
        var recibir = new RecibirEnvioEnDeposito(
            new EnvioRepositorio(contexto),
            new BultoRepositorio(contexto),
            new RecepcionRepositorio(contexto),
            new CambiarEstadoEnvio(
                new EnvioRepositorio(contexto),
                new RutaRepositorio(contexto),
                new RegistrarOutboxEstado(outbox, historial),
                new PublicarOutboxPendientes(outbox, new PublicadorMensajesPrueba { Disponible = false }),
                cache));

        var resultado = await recibir.EjecutarAsync(
            DatosSemilla.OperadorNorteId, envioId, bultosRecibidos: 1, "operario.norte", "falta uno");

        Assert.True(resultado.HayDiscrepancia);
        Assert.Equal(2, resultado.BultosEsperados);
        Assert.Equal(1, resultado.BultosRecibidos);
        Assert.Equal(EstadoEnvio.EnDeposito, (await contexto.Envios.SingleAsync()).Estado);
        Assert.Single(await contexto.RecepcionesEnvio.ToListAsync());
    }

    [Fact]
    public async Task Banderas_CambiarEscribeAuditoriaYCache()
    {
        await using var contexto = Crear($"banderas-{Guid.NewGuid():N}", DatosSemilla.OperadorNorteId);
        var cache = new CacheLecturaMemoria();
        var gestion = new GestionBanderas(new BanderaRepositorio(contexto), cache);

        Assert.False(await gestion.EstaActivaAsync(DatosSemilla.OperadorNorteId, CatalogoBanderas.RetiroDomicilio));

        await gestion.CambiarAsync(
            DatosSemilla.OperadorNorteId,
            CatalogoBanderas.RetiroDomicilio,
            activa: true,
            usuario: "admin.norte");

        Assert.True(await gestion.EstaActivaAsync(DatosSemilla.OperadorNorteId, CatalogoBanderas.RetiroDomicilio));
        var hist = await gestion.HistorialAsync(DatosSemilla.OperadorNorteId);
        var cambio = Assert.Single(hist);
        Assert.Equal(CatalogoBanderas.RetiroDomicilio, cambio.Clave);
        Assert.True(cambio.Activa);
        Assert.Equal("admin.norte", cambio.Usuario);
    }

    private static PlataformaDbContext Crear(string nombreDb, Guid? operadorId)
    {
        var operador = new OperadorActualFijo(operadorId);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase(nombreDb)
            .AddInterceptors(new OperadorSaveChangesInterceptor(operador))
            .Options;
        return new PlataformaDbContext(options, operador);
    }
}
