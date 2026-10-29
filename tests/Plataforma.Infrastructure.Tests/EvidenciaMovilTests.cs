using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public sealed class EvidenciaMovilTests
{
    [Fact]
    public async Task Entrega_PersisteEvidenciaCompletaYNoLaDuplicaEnReintento()
    {
        await using var contexto = await Preparar(EstadoEnvio.EnTransito);
        var envio = await contexto.Envios.SingleAsync();
        var fecha = new DateTimeOffset(2026, 10, 29, 10, 30, 0, TimeSpan.FromHours(-3));
        var evento = new ComprobanteEntrega(Guid.NewGuid(), envio.Id, EstadoEnvio.Entregado,
            "Receptor", "Entregado", fecha, "/9j/2Q==", "[[{\"x\":0.1,\"y\":0.2}]]", -34.9m, -56.2m);

        Assert.Single((await Ejecutar(contexto, evento)).Aplicados);
        Assert.Single((await Ejecutar(contexto, evento)).Aplicados);
        contexto.ChangeTracker.Clear();
        var prueba = Assert.Single(await contexto.PruebasDeEntrega.ToListAsync());
        Assert.Equal(evento.Foto, prueba.Foto);
        Assert.Equal(evento.Firma, prueba.Firma);
        Assert.Equal(evento.Latitud, prueba.Latitud);
        Assert.Equal(evento.Longitud, prueba.Longitud);
        Assert.Equal(fecha, prueba.FechaDispositivo);
        Assert.Equal(fecha.UtcDateTime, prueba.Fecha);
        Assert.Single(await contexto.EventosSincronizacionProcesados.ToListAsync());
    }

    [Fact]
    public async Task IntentoFallido_RequiereMotivoYRegistraUnaVez()
    {
        await using var contexto = await Preparar(EstadoEnvio.EnTransito);
        var envio = await contexto.Envios.SingleAsync();
        var evento = new ComprobanteEntrega(Guid.NewGuid(), envio.Id, EstadoEnvio.NoEntregado,
            "", "", DateTimeOffset.UtcNow);
        Assert.Single((await Ejecutar(contexto, evento)).Rechazados);
        Assert.Empty(await contexto.IntentosEntrega.ToListAsync());

        evento = evento with { Observaciones = "Destinatario ausente" };
        Assert.Single((await Ejecutar(contexto, evento)).Aplicados);
        Assert.Single((await Ejecutar(contexto, evento)).Aplicados);
        contexto.ChangeTracker.Clear();
        var intento = Assert.Single(await contexto.IntentosEntrega.ToListAsync());
        Assert.Equal(evento.Observaciones, intento.Resultado);
        Assert.Equal(1, intento.Numero);
        Assert.Equal(evento.FechaDispositivo.UtcDateTime, intento.Fecha);
        Assert.Equal(1, (await contexto.Envios.SingleAsync()).IntentosRealizados);
        Assert.Single(await contexto.PruebasDeEntrega.ToListAsync());
    }

    [Fact]
    public async Task Carga_ValidaCodigoYPersisteEstadoYComprobante()
    {
        await using var contexto = await Preparar(EstadoEnvio.AsignadoARuta);
        var envio = await contexto.Envios.SingleAsync();
        var evento = new ComprobanteEntrega(Guid.NewGuid(), envio.Id, EstadoEnvio.EnTransito,
            "", "", DateTimeOffset.UtcNow, CodigoEscaneado: "otro");
        Assert.Single((await Ejecutar(contexto, evento)).Rechazados);
        Assert.Equal(EstadoEnvio.AsignadoARuta, envio.Estado);
        evento = evento with { CodigoEscaneado = envio.NumeroSeguimiento };
        Assert.Single((await Ejecutar(contexto, evento)).Aplicados);
        Assert.Equal(EstadoEnvio.EnTransito, envio.Estado);
        Assert.Equal(envio.NumeroSeguimiento, (await contexto.PruebasDeEntrega.SingleAsync()).CodigoEscaneado);
    }

    [Theory]
    [InlineData("no base64", null, null, null)]
    [InlineData("aG9sYQ==", null, null, null)]
    [InlineData(null, "firma no JSON", null, null)]
    [InlineData(null, "{}", null, null)]
    [InlineData(null, null, 91, 20)]
    [InlineData(null, null, 20, null)]
    public async Task EvidenciaInvalida_NoConfirmaNiMuta(string? foto, string? firma, int? latitud, int? longitud)
    {
        await using var contexto = await Preparar(EstadoEnvio.EnTransito);
        var envio = await contexto.Envios.SingleAsync();
        var evento = new ComprobanteEntrega(Guid.NewGuid(), envio.Id, EstadoEnvio.Entregado,
            "Receptor", "", DateTimeOffset.UtcNow, foto, firma, latitud, longitud);
        var resultado = await Ejecutar(contexto, evento);
        Assert.Empty(resultado.Aplicados);
        Assert.Single(resultado.Rechazados);
        Assert.Equal(EstadoEnvio.EnTransito, envio.Estado);
        Assert.Empty(await contexto.PruebasDeEntrega.ToListAsync());
        Assert.Empty(await contexto.EventosSincronizacionProcesados.ToListAsync());
    }

    [Fact]
    public async Task EventoIdAplicado_NoPuedeReutilizarseParaOtroEstado()
    {
        await using var contexto = await Preparar(EstadoEnvio.EnTransito);
        var envio = await contexto.Envios.SingleAsync();
        var evento = new ComprobanteEntrega(Guid.NewGuid(), envio.Id, EstadoEnvio.Entregado,
            "Receptor", "", DateTimeOffset.UtcNow);
        Assert.Single((await Ejecutar(contexto, evento)).Aplicados);
        Assert.Single((await Ejecutar(contexto, evento with { Estado = EstadoEnvio.NoEntregado })).Rechazados);
        Assert.Empty(await contexto.IntentosEntrega.ToListAsync());
    }

    private static Task<ResultadoSincronizacion> Ejecutar(PlataformaDbContext contexto, ComprobanteEntrega evento)
    {
        var cache = new CacheLecturaMemoria();
        var sync = new SincronizarEntregas(
            new RutaRepositorio(contexto), new EnvioRepositorio(contexto),
            new PruebaDeEntregaRepositorio(contexto), new EventoSincronizacionRepositorio(contexto),
            new RegistrarOutboxEstado(new OutboxRepositorio(contexto), new HistorialEstadoRepositorio(contexto)),
            new PublicarOutboxPendientes(new OutboxRepositorio(contexto),
                new PublicadorMensajesPrueba { Disponible = false }),
            new ConfiguracionOperador(new OperadorRepositorio(contexto), cache), cache,
            new IntentoEntregaRepositorio(contexto));
        return sync.Ejecutar(DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId,
            new DateOnly(2026, 10, 29), [evento], "repartidor.norte");
    }

    private static async Task<PlataformaDbContext> Preparar(EstadoEnvio estado)
    {
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase($"evidencia-{Guid.NewGuid():N}").Options;
        var contexto = new PlataformaDbContext(options);
        contexto.Operadores.Add(Operador.Crear(DatosSemilla.OperadorNorteId,
            "Norte", "#123456", 3));
        contexto.Repartidores.Add(Repartidor.Registrar(DatosSemilla.RepartidorNorteId,
            DatosSemilla.OperadorNorteId, "Repartidor", "DOC", "099", DatosSemilla.UsuarioRepartidorNorteId));
        var envio = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "Destino", "Calle 1");
        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        contexto.Envios.Add(envio);
        await contexto.SaveChangesAsync();
        var cache = new CacheLecturaMemoria();
        await new DespacharRuta(new RutaRepositorio(contexto), new EnvioRepositorio(contexto),
            new RepartidorRepositorio(contexto),
            new RegistrarOutboxEstado(new OutboxRepositorio(contexto), new HistorialEstadoRepositorio(contexto)),
            new PublicarOutboxPendientes(new OutboxRepositorio(contexto),
                new PublicadorMensajesPrueba { Disponible = false }), cache)
            .EjecutarAsync(DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId,
                new DateOnly(2026, 10, 29), [envio.Id]);
        if (estado == EstadoEnvio.EnTransito)
            envio.CambiarEstado(estado);
        await contexto.SaveChangesAsync();
        return contexto;
    }
}
