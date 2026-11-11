using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>RF 14: el despacho respeta las franjas comprometidas de los envíos.</summary>
public class DespachoFranjasTests
{
    private static readonly Guid Operador = DatosSemilla.OperadorNorteId;

    [Fact]
    public async Task FranjasDistintas_EnLaMismaRuta_SeRechazaSinAsignarNada()
    {
        await using var ctx = Crear();
        var manana = await Franja(ctx, "Mañana", new TimeOnly(8, 0), new TimeOnly(23, 59));
        var tarde = await Franja(ctx, "Tarde", new TimeOnly(9, 0), new TimeOnly(23, 59));
        var a = await EnDeposito(ctx, manana.Id);
        var b = await EnDeposito(ctx, tarde.Id);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Despacho(ctx).EjecutarAsync(Operador, DatosSemilla.RepartidorNorteId, Manana(), [a, b]));

        Assert.Contains("distintas franjas", error.Message);
        Assert.Equal(0, await ctx.Rutas.CountAsync());
        Assert.All(await ctx.Envios.ToListAsync(), envio => Assert.Null(envio.RutaId));
    }

    [Fact]
    public async Task MismaFranjaYEnviosSinFranja_SeDespachan()
    {
        await using var ctx = Crear();
        var manana = await Franja(ctx, "Mañana", new TimeOnly(8, 0), new TimeOnly(12, 0));
        var a = await EnDeposito(ctx, manana.Id);
        var b = await EnDeposito(ctx, manana.Id);
        var sinFranja = await EnDeposito(ctx, null);

        var ruta = await Despacho(ctx).EjecutarAsync(Operador, DatosSemilla.RepartidorNorteId, Manana(), [a, b, sinFranja]);

        Assert.Equal(3, await ctx.Paradas.CountAsync(parada => parada.RutaId == ruta.Id));
    }

    [Fact]
    public async Task FranjaQueYaTermino_SoloSeRechazaParaHoy()
    {
        await using var ctx = Crear();
        var ahora = DateTimeOffset.UtcNow.ToOffset(RelojOperacion.Offset);
        // Una franja que terminó hace un minuto (salvo en el primer minuto del día, donde no se puede armar).
        if (ahora.TimeOfDay < TimeSpan.FromMinutes(2))
            return;

        var hasta = TimeOnly.FromDateTime(ahora.DateTime).AddMinutes(-1);
        var vencida = await Franja(ctx, "Temprano", new TimeOnly(0, 0), hasta);
        var envio = await EnDeposito(ctx, vencida.Id);
        var hoy = DateOnly.FromDateTime(ahora.DateTime);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Despacho(ctx).EjecutarAsync(Operador, DatosSemilla.RepartidorNorteId, hoy, [envio]));
        Assert.Contains("ya terminó hoy", error.Message);

        var ruta = await Despacho(ctx).EjecutarAsync(Operador, DatosSemilla.RepartidorNorteId, hoy.AddDays(1), [envio]);
        Assert.Equal(hoy.AddDays(1), ruta.Fecha);
    }

    private static DateOnly Manana() =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(RelojOperacion.Offset).DateTime).AddDays(1);

    private static async Task<FranjaHoraria> Franja(PlataformaDbContext ctx, string nombre, TimeOnly desde, TimeOnly hasta)
    {
        var franja = FranjaHoraria.Definir(Guid.NewGuid(), Operador, nombre, desde, hasta);
        ctx.FranjasHorarias.Add(franja);
        await ctx.SaveChangesAsync();
        return franja;
    }

    private static async Task<Guid> EnDeposito(PlataformaDbContext ctx, Guid? franjaId)
    {
        var envio = Envio.Admitir(Operador, DatosSemilla.ComercioNorteId, "Destinatario", "Calle 1", franjaId: franjaId);
        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        ctx.Envios.Add(envio);
        await ctx.SaveChangesAsync();
        return envio.Id;
    }

    private static DespacharRuta Despacho(PlataformaDbContext ctx)
    {
        var outbox = new OutboxRepositorio(ctx);
        return new DespacharRuta(
            new RutaRepositorio(ctx),
            new EnvioRepositorio(ctx),
            new RepartidorRepositorio(ctx),
            new BultoRepositorio(ctx),
            new CoberturaRepositorio(ctx),
            new RegistrarOutboxEstado(outbox, new HistorialEstadoRepositorio(ctx)),
            new PublicarOutboxPendientes(outbox, new PublicadorMensajesPrueba { Disponible = false }),
            new CacheLecturaMemoria());
    }

    private static PlataformaDbContext Crear()
    {
        var operadorActual = new OperadorActualFijo(Operador);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase($"despacho-franjas-{Guid.NewGuid():N}")
            .AddInterceptors(new OperadorSaveChangesInterceptor(operadorActual))
            .Options;
        var ctx = new PlataformaDbContext(options, operadorActual);
        ctx.Repartidores.Add(Repartidor.Registrar(
            DatosSemilla.RepartidorNorteId, Operador, "Repartidor Norte", "DOC-1", "099", DatosSemilla.UsuarioRepartidorNorteId));
        ctx.SaveChanges();
        return ctx;
    }
}
