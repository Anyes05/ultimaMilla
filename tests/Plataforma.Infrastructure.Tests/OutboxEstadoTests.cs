using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Application.Mensajes;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class OutboxEstadoTests
{
    [Fact]
    public async Task CambiarEstadoEnvio_EscribeOutboxEnLaMismaTransaccion()
    {
        var db = $"outbox-{Guid.NewGuid():N}";
        Guid envioId;

        await using (var seed = CrearContexto(db, null))
        {
            var envio = Envio.Admitir(
                DatosSemilla.OperadorNorteId,
                DatosSemilla.ComercioNorteId,
                "Destinatario",
                "Calle 1");
            envioId = envio.Id;
            seed.Envios.Add(envio);
            await seed.SaveChangesAsync();
        }

        await using var contexto = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var publicador = new PublicadorMensajesPrueba { Disponible = true };
        var outbox = new OutboxRepositorio(contexto);
        var casoUso = new CambiarEstadoEnvio(
            new EnvioRepositorio(contexto),
            new RegistrarOutboxEstado(outbox, new HistorialEstadoRepositorio(contexto)),
            new PublicarOutboxPendientes(outbox, publicador),
            new CacheLecturaMemoria());

        var resultado = await casoUso.EjecutarAsync(
            envioId, DatosSemilla.OperadorNorteId, EstadoEnvio.EnDeposito);

        var fila = Assert.Single(await contexto.MensajesOutbox.ToListAsync());
        Assert.Equal(MensajeOutbox.TipoEnvioEstadoCambiado, fila.Tipo);
        Assert.Equal(MensajeOutbox.VersionContratoV1, fila.Version);
        Assert.Equal(envioId, fila.EnvioId);
        Assert.Equal(resultado.EventId, fila.EventId);
        Assert.NotNull(fila.PublicadoEn);
        Assert.Single(publicador.Publicados);

        var mensaje = EnvioEstadoCambiado.Deserializar(fila.Payload);
        Assert.NotNull(mensaje);
        Assert.Equal(fila.EventId, mensaje!.EventId);
        Assert.Equal(EstadoEnvio.Admitido, mensaje.EstadoAnterior);
        Assert.Equal(EstadoEnvio.EnDeposito, mensaje.EstadoNuevo);
        Assert.Equal(DatosSemilla.OperadorNorteId, mensaje.OperadorId);
        Assert.Equal(1, await contexto.HistorialEstados.CountAsync(h => h.EnvioId == envioId));
    }

    [Fact]
    public async Task EventIdDuplicado_SeRechaza()
    {
        var db = $"outbox-dup-{Guid.NewGuid():N}";
        await using var contexto = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var outbox = new OutboxRepositorio(contexto);
        var registrar = new RegistrarOutboxEstado(outbox, new HistorialEstadoRepositorio(contexto));
        var eventId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var envioId = Guid.NewGuid();

        var primero = await registrar.EjecutarAsync(
            DatosSemilla.OperadorNorteId,
            envioId,
            EstadoEnvio.Admitido,
            EstadoEnvio.EnDeposito,
            eventId: eventId);
        await outbox.GuardarAsync();
        Assert.Equal(eventId, primero);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registrar.EjecutarAsync(
                DatosSemilla.OperadorNorteId,
                envioId,
                EstadoEnvio.EnDeposito,
                EstadoEnvio.AsignadoARuta,
                eventId: eventId));
    }

    [Fact]
    public void EnvioEstadoCambiado_SerializaComoContratoV1()
    {
        var mensaje = EnvioEstadoCambiado.Crear(
            DatosSemilla.OperadorNorteId,
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            EstadoEnvio.EnTransito,
            EstadoEnvio.Entregado,
            DateTimeOffset.Parse("2026-10-22T21:00:00-03:00"),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        var json = mensaje.Serializar();

        Assert.Contains("\"eventId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"", json);
        Assert.Contains("\"estadoAnterior\":\"EnTransito\"", json);
        Assert.Contains("\"estadoNuevo\":\"Entregado\"", json);
    }

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
