using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class NadiaBloqueantesTests
{
    [Fact]
    public async Task Repartidores_Abm_ActivaYLista()
    {
        await using var ctx = Crear($"rep-{Guid.NewGuid():N}");
        var gestion = new GestionRepartidores(new RepartidorRepositorio(ctx));
        var r = await gestion.RegistrarAsync(DatosSemilla.OperadorNorteId, "Ana Ruta", "DOC-9", "099");
        Assert.True(r.Activo);
        await gestion.CambiarActivoAsync(DatosSemilla.OperadorNorteId, r.Id, activo: false);
        Assert.Empty(await gestion.ListarAsync(DatosSemilla.OperadorNorteId, soloActivos: true));
        Assert.Single(await gestion.ListarAsync(DatosSemilla.OperadorNorteId, soloActivos: false));
    }

    [Fact]
    public async Task RendirBulto_PorCodigo_PasaADevuelto()
    {
        var db = $"rendir-{Guid.NewGuid():N}";
        await using (var seed = Crear(db))
        {
            seed.Operadores.Add(Operador.Crear(DatosSemilla.OperadorNorteId, "Norte", "azul", 3));
            var envio = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "D", "C");
            envio.CambiarEstado(EstadoEnvio.EnDeposito);
            envio.AsignarARuta(Guid.NewGuid());
            envio.CambiarEstado(EstadoEnvio.EnTransito);
            envio.CambiarEstado(EstadoEnvio.NoEntregado);
            envio.CambiarEstado(EstadoEnvio.EnDevolucion);
            seed.Envios.Add(envio);
            seed.Bultos.Add(Bulto.Crear(Guid.NewGuid(), envio.OperadorId, envio.Id, $"{envio.NumeroSeguimiento}-B01", 1m));
            await seed.SaveChangesAsync();
        }

        await using var ctx = Crear(db);
        var bulto = await ctx.Bultos.SingleAsync();
        var cache = new CacheLecturaMemoria();
        var outbox = new OutboxRepositorio(ctx);
        var historial = new HistorialEstadoRepositorio(ctx);
        var rendir = new RendirBulto(
            new BultoRepositorio(ctx),
            new EnvioRepositorio(ctx),
            outbox,
            new RegistrarOutboxEstado(outbox, historial),
            new PublicarOutboxPendientes(outbox, new PublicadorMensajesPrueba { Disponible = false }),
            cache);

        var resultado = await rendir.PorCodigoAsync(DatosSemilla.OperadorNorteId, bulto.Codigo, "operario");
        Assert.Equal(EstadoBulto.Devuelto, resultado.EstadoBulto);
        Assert.Equal(EstadoEnvio.Devuelto, resultado.EstadoEnvio);
    }

    [Fact]
    public async Task Admitir_ConZonaYFranja_PersisteYEsIdempotentePorClave()
    {
        await using var ctx = Crear($"adm-{Guid.NewGuid():N}");
        ctx.Operadores.Add(Operador.Crear(DatosSemilla.OperadorNorteId, "Norte", "azul", 3));
        ctx.Comercios.Add(Comercio.Registrar(
            DatosSemilla.ComercioNorteId, DatosSemilla.OperadorNorteId, "C", "R", "D", "Dir"));
        var zona = ZonaCobertura.Definir(Guid.NewGuid(), DatosSemilla.OperadorNorteId, "Z1", "[]");
        var franja = FranjaHoraria.Definir(
            Guid.NewGuid(), DatosSemilla.OperadorNorteId, "M", new TimeOnly(9, 0), new TimeOnly(12, 0), zona.Id);
        ctx.ZonasCobertura.Add(zona);
        ctx.FranjasHorarias.Add(franja);
        var reglaId = Guid.NewGuid();
        ctx.ReglasTarifarias.Add(ReglaTarifaria.Crear(reglaId, DatosSemilla.OperadorNorteId, 100, 10, 5, 0, 0));
        ctx.VigenciasTarifa.Add(VigenciaTarifa.Crear(
            Guid.NewGuid(), DatosSemilla.OperadorNorteId, reglaId,
            DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddYears(1)));
        await ctx.SaveChangesAsync();

        var admitir = new AdmitirEnvio(
            new EnvioRepositorio(ctx),
            new BultoRepositorio(ctx),
            new CalcularTarifa(new TarifaRepositorio(ctx)),
            new OutboxRepositorio(ctx),
            new ComercioRepositorio(ctx),
            new CoberturaRepositorio(ctx));

        var envio = await admitir.EjecutarAsync(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.ComercioNorteId,
            "Dest",
            "Calle 1",
            pesoKg: 2,
            zonaId: zona.Id,
            franjaId: franja.Id,
            claveExterna: "IMP-1");

        Assert.Equal(zona.Id, envio.ZonaId);
        Assert.Equal(franja.Id, envio.FranjaId);
        Assert.Equal("IMP-1", envio.ClaveExterna);

        var otra = await admitir.EjecutarAsync(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.ComercioNorteId,
            "Otro",
            "Calle 2",
            pesoKg: 2,
            claveExterna: "IMP-1");
        Assert.Equal(envio.Id, otra.Id);
        Assert.Equal(1, await ctx.Envios.CountAsync());
    }

    private static PlataformaDbContext Crear(string db)
    {
        var operador = new OperadorActualFijo(DatosSemilla.OperadorNorteId);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase(db)
            .AddInterceptors(new OperadorSaveChangesInterceptor(operador))
            .Options;
        return new PlataformaDbContext(options, operador);
    }
}
