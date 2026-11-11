using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>RF 4: documento del receptor, política de devolución y espera entre intentos.</summary>
public sealed class ReglasOperativasRf4Tests
{
    [Fact]
    public async Task ExigeDocumento_SinDocumentoSeRechazaYConDocumentoSeGuarda()
    {
        await using var contexto = await Preparar(maximoIntentos: 3, exigeDocumento: true);
        var envio = await contexto.Envios.SingleAsync();
        var evento = new ComprobanteEntrega(Guid.NewGuid(), envio.Id, EstadoEnvio.Entregado, "Juan Pérez", "", DateTimeOffset.UtcNow);

        var rechazo = Assert.Single((await Ejecutar(contexto, evento)).Rechazados);
        Assert.Contains("documento del receptor", rechazo.Motivo);
        Assert.Empty(await contexto.PruebasDeEntrega.ToListAsync());

        Assert.Single((await Ejecutar(contexto, evento with { EventoId = Guid.NewGuid(), DocumentoReceptor = " 1.234.567-8 " })).Aplicados);
        contexto.ChangeTracker.Clear();
        Assert.Equal("1.234.567-8", (await contexto.PruebasDeEntrega.SingleAsync()).DocumentoReceptor);
    }

    [Fact]
    public async Task SinLaRegla_LaEntregaSinDocumentoSigueFuncionando()
    {
        await using var contexto = await Preparar(maximoIntentos: 3, exigeDocumento: false);
        var envio = await contexto.Envios.SingleAsync();

        Assert.Single((await Ejecutar(contexto,
            new ComprobanteEntrega(Guid.NewGuid(), envio.Id, EstadoEnvio.Entregado, "Juan", "", DateTimeOffset.UtcNow))).Aplicados);
    }

    [Theory]
    [InlineData(PoliticaDevolucion.Automatica, EstadoEnvio.EnDevolucion)]
    [InlineData(PoliticaDevolucion.EsperarDecision, EstadoEnvio.NoEntregado)]
    public async Task AlAgotarIntentos_RespetaLaPoliticaDeDevolucion(PoliticaDevolucion politica, EstadoEnvio esperado)
    {
        await using var contexto = await Preparar(maximoIntentos: 1, politica: politica);
        var envio = await contexto.Envios.SingleAsync();

        Assert.Single((await Ejecutar(contexto,
            new ComprobanteEntrega(Guid.NewGuid(), envio.Id, EstadoEnvio.NoEntregado, "", "Ausente", DateTimeOffset.UtcNow))).Aplicados);

        contexto.ChangeTracker.Clear();
        Assert.Equal(esperado, (await contexto.Envios.SingleAsync()).Estado);
    }

    [Fact]
    public async Task EsperaEntreIntentos_NoDejaVolverASalirAntesDeTiempo()
    {
        await using var contexto = await Preparar(maximoIntentos: 3, plazoEntreIntentos: 4);
        var envio = await contexto.Envios.SingleAsync();
        Assert.Single((await Ejecutar(contexto,
            new ComprobanteEntrega(Guid.NewGuid(), envio.Id, EstadoEnvio.NoEntregado, "", "Ausente", DateTimeOffset.UtcNow))).Aplicados);
        envio.CambiarEstado(EstadoEnvio.Reprogramado, "prueba", "Otra visita");
        await contexto.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Despacho(contexto, conReglas: true).EjecutarAsync(DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId,
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), [envio.Id]));
        Assert.Contains("intento fallido hace poco", error.Message);
    }

    private static Task<ResultadoSincronizacion> Ejecutar(PlataformaDbContext contexto, ComprobanteEntrega evento)
    {
        var cache = new CacheLecturaMemoria();
        var sync = new SincronizarEntregas(
            new RutaRepositorio(contexto), new EnvioRepositorio(contexto),
            new BultoRepositorio(contexto),
            new PruebaDeEntregaRepositorio(contexto), new EventoSincronizacionRepositorio(contexto),
            new OutboxRepositorio(contexto),
            new RegistrarOutboxEstado(new OutboxRepositorio(contexto), new HistorialEstadoRepositorio(contexto)),
            new PublicarOutboxPendientes(new OutboxRepositorio(contexto), new PublicadorMensajesPrueba { Disponible = false }),
            new ConfiguracionOperador(new OperadorRepositorio(contexto), cache),
            new ReglaOperativaRepositorio(contexto),
            new BanderasActivas(),
            cache,
            new IntentoEntregaRepositorio(contexto));
        return sync.Ejecutar(DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId,
            DateOnly.FromDateTime(DateTime.UtcNow), [evento], "repartidor.norte");
    }

    private static DespacharRuta Despacho(PlataformaDbContext contexto, bool conReglas) =>
        new(new RutaRepositorio(contexto), new EnvioRepositorio(contexto),
            new RepartidorRepositorio(contexto),
            new BultoRepositorio(contexto),
            new CoberturaRepositorio(contexto),
            new RegistrarOutboxEstado(new OutboxRepositorio(contexto), new HistorialEstadoRepositorio(contexto)),
            new PublicarOutboxPendientes(new OutboxRepositorio(contexto), new PublicadorMensajesPrueba { Disponible = false }),
            new CacheLecturaMemoria(),
            conReglas ? new HistorialEstadoRepositorio(contexto) : null,
            conReglas ? new ReglaOperativaRepositorio(contexto) : null);

    private static async Task<PlataformaDbContext> Preparar(
        int maximoIntentos,
        bool exigeDocumento = false,
        PoliticaDevolucion politica = PoliticaDevolucion.Automatica,
        int plazoEntreIntentos = 0)
    {
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase($"reglas-rf4-{Guid.NewGuid():N}").Options;
        var contexto = new PlataformaDbContext(options);
        contexto.Operadores.Add(Operador.Crear(DatosSemilla.OperadorNorteId, "Norte", "azul", maximoIntentos));
        contexto.ReglasOperativas.Add(ReglaOperativa.Crear(
            Guid.NewGuid(), DatosSemilla.OperadorNorteId, maximoIntentos, 48, false, false, false, 24,
            DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddYears(1),
            exigeDocumentoReceptor: exigeDocumento,
            plazoEntreIntentosHoras: plazoEntreIntentos,
            politicaDevolucion: politica));
        contexto.Repartidores.Add(Repartidor.Registrar(DatosSemilla.RepartidorNorteId,
            DatosSemilla.OperadorNorteId, "Repartidor", "DOC", "099", DatosSemilla.UsuarioRepartidorNorteId));
        var envio = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "Destino", "Calle 1");
        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        contexto.Envios.Add(envio);
        await contexto.SaveChangesAsync();
        await Despacho(contexto, conReglas: false).EjecutarAsync(
            DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId, DateOnly.FromDateTime(DateTime.UtcNow), [envio.Id]);
        envio.CambiarEstado(EstadoEnvio.EnTransito);
        await contexto.SaveChangesAsync();
        return contexto;
    }

    private sealed class BanderasActivas : IBanderasOperador
    {
        public Task<bool> EstaActivaAsync(Guid operadorId, string clave, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
