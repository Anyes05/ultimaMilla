using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>Alta con detalle por bulto (RF 8), reportes de gestión (RF 30) y envíos en riesgo (RF 29).</summary>
public class ReportesYRiesgoTests
{
    private static readonly Guid Operador = DatosSemilla.OperadorNorteId;
    private static readonly Guid Comercio = DatosSemilla.ComercioNorteId;

    [Fact]
    public async Task Alta_ConDetallePorBulto_GuardaPesoYMedidasYCalculaConElTotal()
    {
        await using var ctx = await CrearAsync(plazoHoras: 48);

        var envio = await Admitir(ctx).EjecutarAsync(
            Operador, Comercio, "Dest", "Calle 1",
            bultosDeclarados: [new BultoDeclarado(1.5m, 10, 20, 30), new BultoDeclarado(2.5m)]);

        var bultos = await ctx.Bultos.Where(bulto => bulto.EnvioId == envio.Id).OrderBy(bulto => bulto.Codigo).ToListAsync();
        Assert.Equal(2, bultos.Count);
        Assert.Equal(1.5m, bultos[0].PesoKg);
        Assert.Equal((10m, 20m, 30m), (bultos[0].AltoCm, bultos[0].AnchoCm, bultos[0].LargoCm));
        Assert.Equal(2.5m, bultos[1].PesoKg);
        // Tarifa de prueba: base 100 + 10 por kg, con el peso total declarado (4 kg).
        Assert.Equal(140m, envio.TarifaCalculada);
    }

    [Fact]
    public async Task Alta_ConUnBultoSinPeso_SeRechazaSinGuardarNada()
    {
        await using var ctx = await CrearAsync(plazoHoras: 48);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Admitir(ctx).EjecutarAsync(
            Operador, Comercio, "Dest", "Calle 1",
            bultosDeclarados: [new BultoDeclarado(1m), new BultoDeclarado(0m)]));
        Assert.Equal(0, await ctx.Envios.CountAsync());
    }

    [Fact]
    public async Task Reportes_CuentanCumplimientoMotivosYLiquidacion()
    {
        await using var ctx = await CrearAsync(plazoHoras: 48);
        var admitir = Admitir(ctx);
        var entregado = await admitir.EjecutarAsync(Operador, Comercio, "A", "Calle 1", pesoKg: 1);
        var fallido = await admitir.EjecutarAsync(Operador, Comercio, "B", "Calle 2", pesoKg: 1);
        await admitir.EjecutarAsync(Operador, Comercio, "C", "Calle 3", pesoKg: 1);

        Avanzar(ctx, entregado, EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito, EstadoEnvio.Entregado);
        Avanzar(ctx, fallido, EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito, EstadoEnvio.NoEntregado);
        ctx.HistorialEstados.Add(HistorialEstado.Registrar(Guid.NewGuid(), Operador, entregado.Id, EstadoEnvio.Entregado, DateTime.UtcNow));
        ctx.HistorialEstados.Add(HistorialEstado.Registrar(Guid.NewGuid(), Operador, fallido.Id, EstadoEnvio.NoEntregado, DateTime.UtcNow, "Ausente"));
        await ctx.SaveChangesAsync();

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var reporte = await Reportes(ctx).EjecutarAsync(Operador, hoy.AddDays(-1), hoy.AddDays(1));

        Assert.Equal(3, reporte.Envios);
        Assert.Equal(1, reporte.Entregados);
        Assert.Equal(2, reporte.EnCurso);
        Assert.Equal(100m, reporte.Total.PorcentajeEntrega);
        Assert.Equal(100m, reporte.Total.PorcentajeATiempo);
        Assert.Equal("Ausente", Assert.Single(reporte.MotivosNoEntrega).Motivo);
        var liquidacion = Assert.Single(reporte.Liquidacion);
        Assert.Equal(1, liquidacion.Entregados);
        Assert.Equal(entregado.TarifaCalculada, liquidacion.Monto);
        Assert.Equal("Sin zona", Assert.Single(reporte.PorZona).Nombre);
    }

    [Fact]
    public async Task Reportes_OtroPeriodo_NoIncluyeLosEnvios()
    {
        await using var ctx = await CrearAsync(plazoHoras: 48);
        await Admitir(ctx).EjecutarAsync(Operador, Comercio, "A", "Calle 1", pesoKg: 1);

        var reporte = await Reportes(ctx).EjecutarAsync(Operador, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));

        Assert.Equal(0, reporte.Envios);
        Assert.Empty(reporte.Volumen);
    }

    [Fact]
    public async Task Riesgo_PlazoCorto_ElEnvioActivoApareceYElEntregadoNo()
    {
        await using var ctx = await CrearAsync(plazoHoras: 1);
        var activo = await Admitir(ctx).EjecutarAsync(Operador, Comercio, "A", "Calle 1", pesoKg: 1);
        var entregado = await Admitir(ctx).EjecutarAsync(Operador, Comercio, "B", "Calle 2", pesoKg: 1);
        Avanzar(ctx, entregado, EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito, EstadoEnvio.Entregado);
        await ctx.SaveChangesAsync();

        var resultado = await Riesgo(ctx).EjecutarAsync(Operador, DateTime.UtcNow, TimeSpan.FromHours(2), 20);

        var riesgo = Assert.Single(resultado.Envios);
        Assert.Equal(activo.Id, riesgo.EnvioId);
        Assert.Equal(1, resultado.Total);
    }

    [Fact]
    public async Task Riesgo_PlazoLargo_NadaEnRiesgo()
    {
        await using var ctx = await CrearAsync(plazoHoras: 48);
        await Admitir(ctx).EjecutarAsync(Operador, Comercio, "A", "Calle 1", pesoKg: 1);

        var resultado = await Riesgo(ctx).EjecutarAsync(Operador, DateTime.UtcNow, TimeSpan.FromHours(2), 20);

        Assert.Equal(0, resultado.Total);
    }

    private static void Avanzar(PlataformaDbContext ctx, Envio envio, params EstadoEnvio[] estados)
    {
        var guardado = ctx.Envios.Local.Single(item => item.Id == envio.Id);
        foreach (var estado in estados)
            guardado.CambiarEstado(estado, "prueba", "prueba");
    }

    private static AdmitirEnvio Admitir(PlataformaDbContext ctx) =>
        new(
            new EnvioRepositorio(ctx),
            new BultoRepositorio(ctx),
            new CalcularTarifa(new TarifaRepositorio(ctx)),
            new OutboxRepositorio(ctx),
            new ComercioRepositorio(ctx),
            new CoberturaRepositorio(ctx));

    private static ObtenerReportesGestion Reportes(PlataformaDbContext ctx) =>
        new(
            new EnvioRepositorio(ctx),
            new HistorialEstadoRepositorio(ctx),
            new ReglaOperativaRepositorio(ctx),
            new RutaRepositorio(ctx),
            new RepartidorRepositorio(ctx),
            new CoberturaRepositorio(ctx),
            new ComercioRepositorio(ctx),
            new OperadorRepositorio(ctx),
            new GestionRetiros(
                new RetiroRepositorio(ctx),
                new BanderasActivas(),
                new OutboxRepositorio(ctx),
                new PublicarOutboxPendientes(new OutboxRepositorio(ctx), new PublicadorMensajesPrueba { Disponible = false }),
                new ComercioRepositorio(ctx)));

    private static ListarEnviosEnRiesgo Riesgo(PlataformaDbContext ctx) =>
        new(
            new EnvioRepositorio(ctx),
            new HistorialEstadoRepositorio(ctx),
            new ReglaOperativaRepositorio(ctx),
            new RutaRepositorio(ctx),
            new CoberturaRepositorio(ctx),
            new OperadorRepositorio(ctx));

    private static async Task<PlataformaDbContext> CrearAsync(int plazoHoras)
    {
        var operadorActual = new OperadorActualFijo(Operador);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase($"reportes-{Guid.NewGuid():N}")
            .AddInterceptors(new OperadorSaveChangesInterceptor(operadorActual))
            .Options;
        var ctx = new PlataformaDbContext(options, operadorActual);

        ctx.Operadores.Add(Plataforma.Domain.Operador.Crear(Operador, "Norte", "azul", 3));
        ctx.Comercios.Add(Plataforma.Domain.Comercio.Registrar(Comercio, Operador, "Comercio Norte", "R", "D", "Dir"));
        var reglaId = Guid.NewGuid();
        ctx.ReglasTarifarias.Add(ReglaTarifaria.Crear(reglaId, Operador, 100, 10, 0, 0, 0));
        ctx.VigenciasTarifa.Add(VigenciaTarifa.Crear(Guid.NewGuid(), Operador, reglaId, DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddYears(1)));
        ctx.ReglasOperativas.Add(ReglaOperativa.Crear(
            Guid.NewGuid(), Operador, 3, plazoHoras, false, false, false, 24,
            DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddYears(1)));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private sealed class BanderasActivas : IBanderasOperador
    {
        public Task<bool> EstaActivaAsync(Guid operadorId, string clave, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
