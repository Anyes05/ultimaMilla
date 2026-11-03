using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class OutboxTraceParentTests
{
    [Fact]
    public async Task RegistrarOutbox_PersisteTraceParentDeLaActivityActual()
    {
        var db = $"outbox-trace-{Guid.NewGuid():N}";
        await using var contexto = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var outbox = new OutboxRepositorio(contexto);
        var registrar = new RegistrarOutboxEstado(outbox, new HistorialEstadoRepositorio(contexto));
        var envioId = Guid.NewGuid();

        using var activity = new Activity("test-cambio");
        activity.Start();
        var esperado = activity.Id;

        var eventId = await registrar.EjecutarAsync(
            DatosSemilla.OperadorNorteId,
            envioId,
            EstadoEnvio.Admitido,
            EstadoEnvio.EnDeposito);
        await outbox.GuardarAsync();

        Assert.NotNull(eventId);
        var fila = Assert.Single(await contexto.MensajesOutbox.ToListAsync());
        Assert.Equal(esperado, fila.TraceParent);
    }

    [Fact]
    public async Task PublicarOutbox_PropagaTraceParentAlPublicador()
    {
        var db = $"outbox-trace-pub-{Guid.NewGuid():N}";
        await using var contexto = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var outbox = new OutboxRepositorio(contexto);
        var registrar = new RegistrarOutboxEstado(outbox, new HistorialEstadoRepositorio(contexto));
        var publicador = new PublicadorMensajesPrueba { Disponible = true };

        using var activity = new Activity("test-pub");
        activity.Start();

        var eventId = await registrar.EjecutarAsync(
            DatosSemilla.OperadorNorteId,
            Guid.NewGuid(),
            EstadoEnvio.Admitido,
            EstadoEnvio.EnDeposito);
        await outbox.GuardarAsync();

        var publicar = new PublicarOutboxPendientes(outbox, publicador);
        Assert.True(await publicar.EjecutarUnoAsync(eventId!.Value));

        var publicado = Assert.Single(publicador.Publicados);
        Assert.Equal(activity.Id, publicado.TraceParent);
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
