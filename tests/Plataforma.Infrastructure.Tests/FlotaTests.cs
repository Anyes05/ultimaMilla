using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>RF 23 y 29: posiciones de los repartidores y flota del día.</summary>
public sealed class FlotaTests
{
    private static readonly Guid Operador = DatosSemilla.OperadorNorteId;

    [Fact]
    public async Task Lote_GuardaLasValidasYDescartaViejasOFuturas()
    {
        await using var ctx = Crear();
        var ahora = DateTimeOffset.UtcNow;

        var resultado = await new RegistrarPosiciones(new PosicionRepositorio(ctx)).EjecutarAsync(Operador, DatosSemilla.RepartidorNorteId,
        [
            new PosicionReportada(-34.9m, -56.16m, ahora.AddMinutes(-2), 8),
            new PosicionReportada(-34.91m, -56.17m, ahora.AddHours(-30)),
            new PosicionReportada(-34.92m, -56.18m, ahora.AddHours(2))
        ]);

        Assert.Equal(new ResultadoPosiciones(1, 2), resultado);
        Assert.Equal(1, await ctx.PosicionesRepartidor.CountAsync());
    }

    [Fact]
    public async Task Lote_ConCoordenadasFueraDeRango_SeRechazaEntero()
    {
        await using var ctx = Crear();

        await Assert.ThrowsAsync<ArgumentException>(() => new RegistrarPosiciones(new PosicionRepositorio(ctx)).EjecutarAsync(
            Operador, DatosSemilla.RepartidorNorteId,
            [new PosicionReportada(-34.9m, -56.1m, DateTimeOffset.UtcNow), new PosicionReportada(95m, 0m, DateTimeOffset.UtcNow)]));
        Assert.Equal(0, await ctx.PosicionesRepartidor.CountAsync());
    }

    [Fact]
    public async Task UltimaPorRepartidor_TomaLaMasReciente()
    {
        await using var ctx = Crear();
        var ahora = DateTimeOffset.UtcNow;
        var otro = Guid.NewGuid();
        await new RegistrarPosiciones(new PosicionRepositorio(ctx)).EjecutarAsync(Operador, DatosSemilla.RepartidorNorteId,
            [new PosicionReportada(-34.90m, -56.10m, ahora.AddMinutes(-20)), new PosicionReportada(-34.95m, -56.15m, ahora.AddMinutes(-1))]);
        await new RegistrarPosiciones(new PosicionRepositorio(ctx)).EjecutarAsync(Operador, otro,
            [new PosicionReportada(-34.80m, -56.00m, ahora.AddMinutes(-5))]);

        var ultimas = await new PosicionRepositorio(ctx).UltimasPorRepartidorAsync(Operador, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(2, ultimas.Count);
        Assert.Equal(-34.95m, ultimas.Single(p => p.RepartidorId == DatosSemilla.RepartidorNorteId).Latitud);
    }

    [Fact]
    public void SinSenal_SinPosicionOConMasDeDiezMinutos()
    {
        var ahora = DateTime.UtcNow;
        Assert.True(new VehiculoEnFlota(Guid.NewGuid(), "A", 1, 0, null, null, null, null).SinSenal(ahora));
        Assert.True(new VehiculoEnFlota(Guid.NewGuid(), "A", 1, 0, 1, 1, null, ahora.AddMinutes(-11)).SinSenal(ahora));
        Assert.False(new VehiculoEnFlota(Guid.NewGuid(), "A", 1, 0, 1, 1, null, ahora.AddMinutes(-3)).SinSenal(ahora));
    }

    private static PlataformaDbContext Crear()
    {
        var operadorActual = new OperadorActualFijo(Operador);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase($"flota-{Guid.NewGuid():N}")
            .AddInterceptors(new OperadorSaveChangesInterceptor(operadorActual))
            .Options;
        return new PlataformaDbContext(options, operadorActual);
    }
}
