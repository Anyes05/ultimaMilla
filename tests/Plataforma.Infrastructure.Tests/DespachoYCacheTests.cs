using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class DespachoYCacheTests
{
    [Fact]
    public async Task DespacharRuta_CreaParadasYPublicaSeguimientoEnCache()
    {
        var db = NombreDb();
        var cache = new CacheLecturaMemoria();
        Guid envioId;
        string token;

        await using (var seed = CrearContexto(db, null))
        {
            seed.Repartidores.Add(Repartidor.Registrar(
                DatosSemilla.RepartidorNorteId,
                DatosSemilla.OperadorNorteId,
                "Repartidor Norte",
                "DOC-1",
                "099",
                DatosSemilla.UsuarioRepartidorNorteId));

            var envio = Envio.Admitir(
                DatosSemilla.OperadorNorteId,
                DatosSemilla.ComercioNorteId,
                "Destinatario",
                "Calle 1");
            envio.CambiarEstado(EstadoEnvio.EnDeposito);
            envioId = envio.Id;
            token = envio.TokenSeguimientoPublico;
            seed.Envios.Add(envio);
            await seed.SaveChangesAsync();
        }

        await using var contexto = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var despacho = new DespacharRuta(
            new RutaRepositorio(contexto),
            new EnvioRepositorio(contexto),
            new RepartidorRepositorio(contexto),
            cache);

        var ruta = await despacho.EjecutarAsync(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.RepartidorNorteId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            [envioId]);

        Assert.NotEqual(Guid.Empty, ruta.Id);
        Assert.Equal(1, await contexto.Paradas.CountAsync(parada => parada.RutaId == ruta.Id));
        Assert.NotNull(await cache.LeerAsync(ClavesCache.Seguimiento(token)));

        var seguimiento = new ObtenerSeguimientoEnvio(new EnvioRepositorio(contexto), cache);
        var visible = await seguimiento.PorTokenAsync(token);
        Assert.NotNull(visible);
        Assert.Equal(EstadoEnvio.AsignadoARuta, visible!.Estado);

        var stats = cache.Estadisticas();
        Assert.True(stats.Hits >= 1);
        Assert.True(stats.Misses >= 0);
        Assert.True(stats.Ratio >= 0);
    }

    [Fact]
    public async Task ObtenerRutasDeHoy_UsaCacheDeOperadorYDeRutas()
    {
        var db = NombreDb();
        var cache = new CacheLecturaMemoria();
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid envioId;

        await using (var seed = CrearContexto(db, null))
        {
            seed.Operadores.Add(Operador.Crear(
                DatosSemilla.OperadorNorteId,
                DatosSemilla.NorteNombre,
                DatosSemilla.NorteColor,
                DatosSemilla.NorteMaximoIntentos));
            seed.Repartidores.Add(Repartidor.Registrar(
                DatosSemilla.RepartidorNorteId,
                DatosSemilla.OperadorNorteId,
                "Repartidor Norte",
                "DOC-1",
                "099",
                DatosSemilla.UsuarioRepartidorNorteId));

            var envio = Envio.Admitir(
                DatosSemilla.OperadorNorteId,
                DatosSemilla.ComercioNorteId,
                "Destinatario",
                "Calle 1");
            envio.CambiarEstado(EstadoEnvio.EnDeposito);
            envioId = envio.Id;
            seed.Envios.Add(envio);
            await seed.SaveChangesAsync();
        }

        await using var contexto = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var envios = new EnvioRepositorio(contexto);
        var rutas = new RutaRepositorio(contexto);
        var repartidores = new RepartidorRepositorio(contexto);
        await new DespacharRuta(rutas, envios, repartidores, cache).EjecutarAsync(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.RepartidorNorteId,
            fecha,
            [envioId]);

        var config = new ConfiguracionOperador(new OperadorRepositorio(contexto), cache);
        var obtener = new ObtenerRutasDeHoy(rutas, envios, config, cache);

        var primera = await obtener.Ejecutar(DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId, fecha);
        var segunda = await obtener.Ejecutar(DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId, fecha);

        Assert.Single(primera);
        Assert.Single(segunda);
        Assert.Equal(primera[0].Id, segunda[0].Id);
        Assert.True(cache.Estadisticas().Hits >= 1);
    }

    [Fact]
    public async Task SincronizarEntregas_ActualizaSeguimientoYDevuelveEstadoActualEnRechazos()
    {
        var db = NombreDb();
        var cache = new CacheLecturaMemoria();
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid envioId;
        string token;

        await using (var seed = CrearContexto(db, null))
        {
            seed.Operadores.Add(Operador.Crear(
                DatosSemilla.OperadorNorteId,
                DatosSemilla.NorteNombre,
                DatosSemilla.NorteColor,
                DatosSemilla.NorteMaximoIntentos));
            seed.Repartidores.Add(Repartidor.Registrar(
                DatosSemilla.RepartidorNorteId,
                DatosSemilla.OperadorNorteId,
                "Repartidor Norte",
                "DOC-1",
                "099",
                DatosSemilla.UsuarioRepartidorNorteId));

            var envio = Envio.Admitir(
                DatosSemilla.OperadorNorteId,
                DatosSemilla.ComercioNorteId,
                "Destinatario",
                "Calle 1");
            envio.CambiarEstado(EstadoEnvio.EnDeposito);
            envioId = envio.Id;
            token = envio.TokenSeguimientoPublico;
            seed.Envios.Add(envio);
            await seed.SaveChangesAsync();
        }

        await using var contexto = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var envios = new EnvioRepositorio(contexto);
        var rutas = new RutaRepositorio(contexto);
        await new DespacharRuta(rutas, envios, new RepartidorRepositorio(contexto), cache).EjecutarAsync(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.RepartidorNorteId,
            fecha,
            [envioId]);

        var envioDespachado = await contexto.Envios.SingleAsync(e => e.Id == envioId);
        envioDespachado.CambiarEstado(EstadoEnvio.EnTransito);
        await contexto.SaveChangesAsync();

        var sync = new SincronizarEntregas(
            rutas,
            envios,
            new PruebaDeEntregaRepositorio(contexto),
            new ConfiguracionOperador(new OperadorRepositorio(contexto), cache),
            cache);

        var ok = await sync.Ejecutar(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.RepartidorNorteId,
            fecha,
            [
                new ComprobanteEntrega(
                    Guid.NewGuid(),
                    envioId,
                    EstadoEnvio.Entregado,
                    "Receptor",
                    "",
                    DateTimeOffset.UtcNow)
            ],
            "repartidor.norte");

        Assert.Single(ok.Aplicados);
        Assert.Equal(1, await contexto.PruebasDeEntrega.CountAsync(p => p.EnvioId == envioId));
        Assert.Equal(EstadoEnvio.Entregado,
            (await new ObtenerSeguimientoEnvio(envios, cache).PorTokenAsync(token))!.Estado);

        var conflicto = await sync.Ejecutar(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.RepartidorNorteId,
            fecha,
            [
                new ComprobanteEntrega(
                    Guid.NewGuid(),
                    envioId,
                    EstadoEnvio.Entregado,
                    "Otro",
                    "",
                    DateTimeOffset.UtcNow)
            ],
            "repartidor.norte");

        var rechazo = Assert.Single(conflicto.Rechazados);
        Assert.Equal(EstadoEnvio.Entregado, rechazo.EstadoActual);
    }

    private static string NombreDb() => $"despacho-{Guid.NewGuid():N}";

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
