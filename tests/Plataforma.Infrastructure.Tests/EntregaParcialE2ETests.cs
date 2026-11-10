using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>Punta a punta InMemory: despacho → sync parcial → redespacho de pendientes.</summary>
public class EntregaParcialE2ETests
{
    [Fact]
    public async Task Parcial_LiberaRutaYPermiteRedespachoDePendientes()
    {
        var db = $"parcial-e2e-{Guid.NewGuid():N}";
        var fecha = new DateOnly(2026, 11, 10);
        Guid envioId;
        Guid bulto1;
        Guid bulto2;

        await using (var seed = Crear(db, null))
        {
            seed.Operadores.Add(Operador.Crear(DatosSemilla.OperadorNorteId, "Norte", "azul", 3));
            seed.Repartidores.Add(Repartidor.Registrar(
                DatosSemilla.RepartidorNorteId, DatosSemilla.OperadorNorteId,
                "Rep", "DOC", "099", DatosSemilla.UsuarioRepartidorNorteId));
            seed.ReglasOperativas.Add(ReglaOperativa.Crear(
                Guid.NewGuid(), DatosSemilla.OperadorNorteId, 3, 48,
                exigeFirma: false, exigeFoto: false, exigeGeocerca: false, 24,
                DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddYears(1),
                admiteEntregaParcial: true,
                politicaBultosPendientes: PoliticaBultosPendientes.NuevaVisitaSinRecargo));

            var envio = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "Dest", "Calle 1");
            envio.CambiarEstado(EstadoEnvio.EnDeposito);
            envioId = envio.Id;
            bulto1 = Guid.NewGuid();
            bulto2 = Guid.NewGuid();
            seed.Envios.Add(envio);
            seed.Bultos.Add(Bulto.Crear(bulto1, envio.OperadorId, envio.Id, "B1", pesoKg: 1m));
            seed.Bultos.Add(Bulto.Crear(bulto2, envio.OperadorId, envio.Id, "B2", pesoKg: 1m));
            await seed.SaveChangesAsync();
        }

        await using var contexto = Crear(db, DatosSemilla.OperadorNorteId);
        var cache = new CacheLecturaMemoria();
        var outbox = new OutboxRepositorio(contexto);
        var historial = new HistorialEstadoRepositorio(contexto);
        var publicar = new PublicarOutboxPendientes(outbox, new PublicadorMensajesPrueba { Disponible = false });
        var registrar = new RegistrarOutboxEstado(outbox, historial);

        await new DespacharRuta(
            new RutaRepositorio(contexto),
            new EnvioRepositorio(contexto),
            new RepartidorRepositorio(contexto),
            new BultoRepositorio(contexto),
            new CoberturaRepositorio(contexto),
            registrar,
            publicar,
            cache).EjecutarAsync(DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId, fecha, [envioId]);

        var envioTrasDespacho = await contexto.Envios.SingleAsync();
        Assert.Equal(EstadoEnvio.AsignadoARuta, envioTrasDespacho.Estado);
        Assert.NotNull(envioTrasDespacho.RutaId);
        envioTrasDespacho.CambiarEstado(EstadoEnvio.EnTransito);
        await contexto.SaveChangesAsync();

        var configuracion = new ConfiguracionOperador(new OperadorRepositorio(contexto), cache);
        var rutasMovil = await new ObtenerRutasDeHoy(
            new RutaRepositorio(contexto),
            new EnvioRepositorio(contexto),
            configuracion,
            new BanderasSiempreActivas(),
            cache,
            new BultoRepositorio(contexto),
            new ReglaOperativaRepositorio(contexto))
            .Ejecutar(DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId, fecha);
        var rutaMovil = Assert.Single(rutasMovil);
        Assert.Contains("entrega-parcial", rutaMovil.Operador.Funcionalidades!);
        Assert.True(rutaMovil.Operador.AdmiteEntregaParcial);
        Assert.Contains("Ausente", rutaMovil.Operador.MotivosNoEntrega!);
        Assert.Equal(2, Assert.Single(rutaMovil.Envios).Bultos!.Count);

        var sync = new SincronizarEntregas(
            new RutaRepositorio(contexto),
            new EnvioRepositorio(contexto),
            new BultoRepositorio(contexto),
            new PruebaDeEntregaRepositorio(contexto),
            new EventoSincronizacionRepositorio(contexto),
            outbox,
            registrar,
            publicar,
            new ConfiguracionOperador(new OperadorRepositorio(contexto), cache),
            new ReglaOperativaRepositorio(contexto),
            new BanderasSiempreActivas(),
            cache,
            new IntentoEntregaRepositorio(contexto));

        var resultado = await sync.Ejecutar(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.RepartidorNorteId,
            fecha,
            [
                new ComprobanteEntrega(
                    Guid.NewGuid(),
                    envioId,
                    EstadoEnvio.EntregadoParcialmente,
                    "Receptor",
                    "parcial",
                    DateTimeOffset.UtcNow,
                    Bultos:
                    [
                        new ResultadoBultoSync(bulto1, EstadoBulto.Entregado),
                        new ResultadoBultoSync(bulto2, EstadoBulto.NoEntregado, "Ausente")
                    ])
            ],
            "repartidor.norte");

        Assert.Single(resultado.Aplicados);
        Assert.Empty(resultado.Rechazados);

        contexto.ChangeTracker.Clear();
        var parcial = await contexto.Envios.SingleAsync();
        Assert.Equal(EstadoEnvio.EntregadoParcialmente, parcial.Estado);
        Assert.Null(parcial.RutaId);

        var bultos = await contexto.Bultos.Where(b => b.EnvioId == envioId).ToListAsync();
        Assert.Contains(bultos, b => b.Id == bulto1 && b.Estado == EstadoBulto.Entregado);
        Assert.Contains(bultos, b => b.Id == bulto2 && b.Estado == EstadoBulto.PendienteNuevaVisita);
        var pruebaBulto = await contexto.PruebasDeEntrega.SingleAsync();
        Assert.Equal(bulto1, pruebaBulto.BultoId);

        await new DespacharRuta(
            new RutaRepositorio(contexto),
            new EnvioRepositorio(contexto),
            new RepartidorRepositorio(contexto),
            new BultoRepositorio(contexto),
            new CoberturaRepositorio(contexto),
            registrar,
            publicar,
            cache).EjecutarAsync(DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId, fecha.AddDays(1), [envioId]);

        contexto.ChangeTracker.Clear();
        var redespachado = await contexto.Envios.SingleAsync();
        Assert.Equal(EstadoEnvio.AsignadoARuta, redespachado.Estado);
        Assert.NotNull(redespachado.RutaId);
    }

    private sealed class BanderasSiempreActivas : IBanderasOperador
    {
        public Task<bool> EstaActivaAsync(Guid operadorId, string clave, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
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
