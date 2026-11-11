using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>RF 3: recargo propio por zona y bonificaciones, versionados con la tarifa.</summary>
public sealed class TarifaZonaYBonificacionTests
{
    private static readonly Guid Operador = DatosSemilla.OperadorNorteId;
    private static readonly Guid Centro = Guid.NewGuid();
    private static readonly Guid Este = Guid.NewGuid();
    private static readonly Guid Mayorista = Guid.NewGuid();

    [Fact]
    public async Task Zona_ConRecargoPropioOGeneral()
    {
        await using var ctx = await CrearAsync();
        var calcular = new CalcularTarifa(new TarifaRepositorio(ctx), new EnvioRepositorio(ctx));

        // Base 100; recargo general de zona 20; Centro tiene 50 propio.
        Assert.Equal(150m, (await calcular.EjecutarConReferenciaAsync(Operador, zonaId: Centro)).Importe);
        Assert.Equal(120m, (await calcular.EjecutarConReferenciaAsync(Operador, zonaId: Este)).Importe);
        Assert.Equal(100m, (await calcular.EjecutarConReferenciaAsync(Operador)).Importe);
    }

    [Fact]
    public async Task Bonificacion_PorComercio_YSeAplicaLaMayorSinAcumular()
    {
        await using var ctx = await CrearAsync();
        var calcular = new CalcularTarifa(new TarifaRepositorio(ctx), new EnvioRepositorio(ctx));

        var mayorista = await calcular.EjecutarConReferenciaAsync(Operador, comercioId: Mayorista);
        Assert.Equal((90m, 10m, "Mayorista"), (mayorista.Importe, mayorista.Descuento, mayorista.Bonificacion));

        var otro = await calcular.EjecutarConReferenciaAsync(Operador, comercioId: Guid.NewGuid());
        Assert.Equal(100m, otro.Importe);
        Assert.Null(otro.Bonificacion);
    }

    [Fact]
    public async Task Bonificacion_PorVolumen_DesdeElEnvioNDelMes()
    {
        await using var ctx = await CrearAsync();
        var comercio = Guid.NewGuid();
        var calcular = new CalcularTarifa(new TarifaRepositorio(ctx), new EnvioRepositorio(ctx));

        // "Volumen": 5 % desde el tercer envío del mes, para cualquier comercio.
        Assert.Null((await calcular.EjecutarConReferenciaAsync(Operador, comercioId: comercio)).Bonificacion);
        for (var i = 0; i < 2; i++)
            ctx.Envios.Add(Envio.Admitir(Operador, comercio, "D", "C"));
        await ctx.SaveChangesAsync();

        var tercero = await calcular.EjecutarConReferenciaAsync(Operador, comercioId: comercio);
        Assert.Equal(("Volumen", 95m), (tercero.Bonificacion, tercero.Importe));
    }

    [Fact]
    public async Task NuevaVersion_SinCondiciones_NoArrastraLasAnteriores()
    {
        await using var ctx = await CrearAsync();
        await new GestionTarifas(new TarifaRepositorio(ctx), new CacheLecturaMemoria()).PublicarVersionAsync(
            Operador, new NuevaVersionTarifa(200, 0, 0, 0, 0), DateTime.UtcNow.AddSeconds(1), "admin");
        await Task.Delay(1100);

        var resultado = await new CalcularTarifa(new TarifaRepositorio(ctx), new EnvioRepositorio(ctx))
            .EjecutarConReferenciaAsync(Operador, zonaId: Centro, comercioId: Mayorista);
        Assert.Equal((200m, 0m, 0m), (resultado.Importe, resultado.RecargoZona, resultado.Descuento));
    }

    [Fact]
    public async Task Retiro_TarifaPropia_ConBonificacionDelComercio_YSeLiquida()
    {
        await using var ctx = await CrearAsync();
        var calcular = new CalcularTarifa(new TarifaRepositorio(ctx), new EnvioRepositorio(ctx));

        // Precio por retiro 60: el comercio Mayorista tiene 10 %; otro comercio paga entero.
        var mayorista = await calcular.CalcularRetiroAsync(Operador, Mayorista);
        Assert.Equal((54m, "Mayorista"), (mayorista.Importe, mayorista.Bonificacion));
        Assert.Equal(60m, (await calcular.CalcularRetiroAsync(Operador, Guid.NewGuid())).Importe);

        var retiros = new RetiroRepositorio(ctx);
        var retiro = SolicitudRetiro.Solicitar(Guid.NewGuid(), Operador, Mayorista, "Calle 1", "", DateOnly.FromDateTime(DateTime.UtcNow), "", mayorista.Importe, mayorista.ReglaTarifariaId);
        retiro.Completar();
        await retiros.AgregarAsync(retiro);
        ctx.Envios.Add(Envio.Admitir(Operador, Guid.NewGuid(), "D", "C"));
        await retiros.GuardarAsync();

        var liquidacion = await new GestionLiquidaciones(new LiquidacionRepositorio(ctx), new EnvioRepositorio(ctx), retiros)
            .AbrirAsync(Operador, Mayorista, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.Equal(54m, liquidacion.Monto);
    }

    [Fact]
    public void Retiro_TarifaNegativa_SeRechaza() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReglaTarifaria.Crear(Guid.NewGuid(), Operador, 1, 0, 0, 0, 0, precioRetiro: -1));

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Bonificacion_PorcentajeFueraDeRango_SeRechaza(int porcentaje) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BonificacionTarifa.Crear(Guid.NewGuid(), Operador, Guid.NewGuid(), "X", porcentaje));

    private static async Task<PlataformaDbContext> CrearAsync()
    {
        var operadorActual = new OperadorActualFijo(Operador);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase($"tarifa-zona-{Guid.NewGuid():N}")
            .AddInterceptors(new OperadorSaveChangesInterceptor(operadorActual))
            .Options;
        var ctx = new PlataformaDbContext(options, operadorActual);
        ctx.Operadores.Add(Plataforma.Domain.Operador.Crear(Operador, "Norte", "azul", 3));
        await ctx.SaveChangesAsync();

        await new GestionTarifas(new TarifaRepositorio(ctx), new CacheLecturaMemoria()).PublicarVersionAsync(
            Operador,
            new NuevaVersionTarifa(100, 0, 0, 0, 0, 0, 20,
                [new RecargoZonaDato(Centro, 50)],
                [new BonificacionDato("Mayorista", 10, Mayorista), new BonificacionDato("Chica", 4, Mayorista), new BonificacionDato("Volumen", 5, null, 3)],
                PrecioRetiro: 60),
            DateTime.UtcNow.AddMinutes(-1),
            "admin");
        return ctx;
    }
}
