using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Application.Mensajes;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class ReasignarEnvioTests
{
    [Fact]
    public async Task Reasignar_EscribeOutboxEnvioReasignado()
    {
        var db = $"reasign-{Guid.NewGuid():N}";
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid envioId;

        await using (var seed = Crear(db, null))
        {
            seed.Repartidores.Add(Repartidor.Registrar(
                DatosSemilla.RepartidorNorteId, DatosSemilla.OperadorNorteId,
                "Norte", "DOC-1", "099", DatosSemilla.UsuarioRepartidorNorteId));
            seed.Repartidores.Add(Repartidor.Registrar(
                Guid.Parse("10000000-0000-0000-0000-000000000099"), DatosSemilla.OperadorNorteId,
                "Otro", "DOC-2", "098", Guid.NewGuid()));

            var envio = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "Dest", "Calle 1");
            envio.CambiarEstado(EstadoEnvio.EnDeposito);
            envioId = envio.Id;
            seed.Envios.Add(envio);
            await seed.SaveChangesAsync();
        }

        await using var contexto = Crear(db, DatosSemilla.OperadorNorteId);
        var cache = new CacheLecturaMemoria();
        await new DespacharRuta(
            new RutaRepositorio(contexto),
            new EnvioRepositorio(contexto),
            new RepartidorRepositorio(contexto),
            new BultoRepositorio(contexto),
            new CoberturaRepositorio(contexto),
            new RegistrarOutboxEstado(new OutboxRepositorio(contexto), new HistorialEstadoRepositorio(contexto)),
            new PublicarOutboxPendientes(new OutboxRepositorio(contexto), new PublicadorMensajesPrueba { Disponible = false }),
            cache).EjecutarAsync(DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId, fecha, [envioId]);

        var resultado = await new ReasignarEnvio(
            new EnvioRepositorio(contexto),
            new RutaRepositorio(contexto),
            new RepartidorRepositorio(contexto),
            new OutboxRepositorio(contexto),
            new PublicarOutboxPendientes(new OutboxRepositorio(contexto), new PublicadorMensajesPrueba { Disponible = false }),
            cache).EjecutarAsync(
            DatosSemilla.OperadorNorteId,
            envioId,
            Guid.Parse("10000000-0000-0000-0000-000000000099"),
            fecha);

        Assert.NotEqual(Guid.Empty, resultado.EventId);
        var mensaje = Assert.Single(await contexto.MensajesOutbox.Where(m => m.Tipo == EnvioReasignado.Tipo).ToListAsync());
        Assert.Equal(resultado.EventId, mensaje.EventId);
        Assert.Contains("repartidorNuevoId", mensaje.Payload);
    }

    private static PlataformaDbContext Crear(string nombreDb, Guid? operadorId)
    {
        var operador = new OperadorActualFijo(operadorId);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase(nombreDb)
            .AddInterceptors(new OperadorSaveChangesInterceptor(operador))
            .Options;
        return new PlataformaDbContext(options, operador);
    }
}
