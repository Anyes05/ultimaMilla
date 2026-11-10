using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>Seguimiento público (letra 4, RF 25 y 26): sin sesión, sin datos personales y con las reglas del operador.</summary>
public sealed class SeguimientoPublicoTests
{
    private readonly string _db = $"seguimiento-{Guid.NewGuid():N}";
    private readonly CacheLecturaMemoria _cache = new();

    [Fact]
    public async Task Detalle_MuestraMarcaEHistorialSinDatosPersonalesNiIdentificadoresInternos()
    {
        var envio = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "Maria Privada", "Calle Secreta 99");
        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        await Sembrar(envio, maximoIntentos: 3, plazoHoras: 24, (EstadoEnvio.EnDeposito, DateTime.UtcNow.AddHours(-1)));

        var detalle = await Consultar(envio);

        Assert.Equal("Operador Norte", detalle!.Operador);
        Assert.Equal("azul", detalle.ColorMarca);
        Assert.Equal(EstadoEnvio.EnDeposito, Assert.Single(detalle.Historial).Estado);
        Assert.NotNull(detalle.EstimadaAntesDeUtc);

        var json = JsonSerializer.Serialize(detalle);
        Assert.DoesNotContain("Maria", json);
        Assert.DoesNotContain("Secreta", json);
        Assert.DoesNotContain(envio.Id.ToString(), json);
        Assert.DoesNotContain(envio.TokenSeguimientoPublico, json);
        Assert.DoesNotContain(envio.OperadorId.ToString(), json);
    }

    [Fact]
    public async Task ConRuta_LaVentanaEsElDiaDeLaRuta()
    {
        var ruta = Ruta.Programar(DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId, new DateOnly(2026, 11, 20));
        var envio = EnTransito(ruta.Id);
        await Sembrar(envio, maximoIntentos: 3, plazoHoras: 24, ruta: ruta);

        var detalle = await Consultar(envio);

        Assert.Equal(new DateOnly(2026, 11, 20), detalle!.FechaPrevista);
        Assert.Null(detalle.EstimadaAntesDeUtc);
    }

    [Fact]
    public async Task Bultos_MuestraCuantosSeEntregaron()
    {
        var envio = EnTransito(Guid.NewGuid());
        var entregado = Bulto.Crear(Guid.NewGuid(), envio.OperadorId, envio.Id, "B1", pesoKg: 1m);
        entregado.CambiarEstado(EstadoBulto.EnRuta);
        entregado.CambiarEstado(EstadoBulto.Entregado);
        await Sembrar(envio, maximoIntentos: 3, plazoHoras: 24, bultos: [entregado, Bulto.Crear(Guid.NewGuid(), envio.OperadorId, envio.Id, "B2", pesoKg: 1m)]);

        var detalle = await Consultar(envio);

        Assert.Equal(1, detalle!.BultosEntregados);
        Assert.Equal(2, detalle.BultosTotal);
    }

    [Fact]
    public async Task Historial_ExponeMotivoYResponsable()
    {
        var envio = NoEntregado(intentos: 1, maximo: 3);
        await Sembrar(envio, maximoIntentos: 3, plazoHoras: 24);

        await using (var contexto = Contexto())
        {
            contexto.HistorialEstados.Add(HistorialEstado.Registrar(
                Guid.NewGuid(),
                envio.OperadorId,
                envio.Id,
                EstadoEnvio.NoEntregado,
                DateTime.UtcNow.AddHours(-1),
                "Nadie en domicilio",
                "repartidor.norte"));
            await contexto.SaveChangesAsync();
        }

        var detalle = await Consultar(envio);
        var evento = Assert.Single(detalle!.Historial);
        Assert.Equal("Nadie en domicilio", evento.Motivo);
        Assert.Equal("repartidor.norte", evento.Responsable);
    }

    [Fact]
    public async Task NoEntregadoDentroDelPlazo_ElDestinatarioPideOtraVisita()
    {
        var envio = NoEntregado(intentos: 1, maximo: 3);
        await Sembrar(envio, maximoIntentos: 3, plazoHoras: 24, (EstadoEnvio.NoEntregado, DateTime.UtcNow.AddHours(-1)));
        Assert.True((await Consultar(envio))!.PuedeReprogramar);

        Assert.Equal(ResultadoReprogramacion.Realizada, await Reprogramar(envio.TokenSeguimientoPublico));

        await using var contexto = Contexto();
        var guardado = await contexto.Envios.SingleAsync(item => item.Id == envio.Id);
        Assert.Equal(EstadoEnvio.Reprogramado, guardado.Estado);
        Assert.Null(guardado.RutaId);
        Assert.Single(await contexto.MensajesOutbox.Where(item => item.EnvioId == envio.Id).ToListAsync());

        // El caché se descarta con el cambio: el destinatario ve el estado nuevo enseguida.
        Assert.Equal(EstadoEnvio.Reprogramado, (await Consultar(envio))!.Estado);
    }

    [Fact]
    public async Task SinIntentosDisponibles_NoSePuedeReprogramar()
    {
        var envio = NoEntregado(intentos: 1, maximo: 1);
        await Sembrar(envio, maximoIntentos: 1, plazoHoras: 24, (EstadoEnvio.NoEntregado, DateTime.UtcNow.AddHours(-1)));

        Assert.False((await Consultar(envio))!.PuedeReprogramar);
        Assert.Equal(ResultadoReprogramacion.NoPermitida, await Reprogramar(envio.TokenSeguimientoPublico));
    }

    [Fact]
    public async Task FueraDelPlazo_NoSePuedeReprogramar()
    {
        var envio = NoEntregado(intentos: 1, maximo: 3);
        await Sembrar(envio, maximoIntentos: 3, plazoHoras: 12, (EstadoEnvio.NoEntregado, DateTime.UtcNow.AddHours(-13)));

        Assert.False((await Consultar(envio))!.PuedeReprogramar);
        Assert.Equal(ResultadoReprogramacion.NoPermitida, await Reprogramar(envio.TokenSeguimientoPublico));
    }

    [Fact]
    public async Task EnvioEnTransito_NoSePuedeReprogramar()
    {
        var envio = EnTransito(Guid.NewGuid());
        await Sembrar(envio, maximoIntentos: 3, plazoHoras: 24);

        Assert.Equal(ResultadoReprogramacion.NoPermitida, await Reprogramar(envio.TokenSeguimientoPublico));
    }

    [Fact]
    public async Task TokenInexistente_NoSeEncuentraNadaParaCambiar()
    {
        Assert.Equal(ResultadoReprogramacion.NoEncontrado, await Reprogramar("token-inventado"));
    }

    private static Envio EnTransito(Guid rutaId)
    {
        var envio = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "Ana Norte", "Calle Norte 1");
        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        envio.AsignarARuta(rutaId);
        envio.CambiarEstado(EstadoEnvio.EnTransito);
        return envio;
    }

    private static Envio NoEntregado(int intentos, int maximo)
    {
        var envio = EnTransito(Guid.NewGuid());
        for (var i = 0; i < intentos; i++)
            envio.RegistrarIntentoFallido(maximo);
        envio.CambiarEstado(EstadoEnvio.NoEntregado);
        return envio;
    }

    private async Task Sembrar(
        Envio envio,
        int maximoIntentos,
        int plazoHoras,
        (EstadoEnvio Estado, DateTime Fecha)? evento = null,
        Ruta? ruta = null,
        IReadOnlyList<Bulto>? bultos = null)
    {
        await using var contexto = Contexto();
        contexto.Operadores.Add(Operador.Crear(DatosSemilla.OperadorNorteId, "Operador Norte", "azul", maximoIntentos));
        contexto.ReglasOperativas.Add(ReglaOperativa.Crear(
            Guid.NewGuid(), DatosSemilla.OperadorNorteId, maximoIntentos, plazoEntregaHoras: 48, exigeFirma: true, exigeFoto: true,
            exigeGeocerca: false, plazoHoras, DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddYears(1)));
        contexto.Envios.Add(envio);
        if (evento is { } dato)
            contexto.HistorialEstados.Add(HistorialEstado.Registrar(Guid.NewGuid(), envio.OperadorId, envio.Id, dato.Estado, dato.Fecha));
        if (ruta is not null)
            contexto.Rutas.Add(ruta);
        if (bultos is not null)
            contexto.Bultos.AddRange(bultos);
        await contexto.SaveChangesAsync();
    }

    private async Task<DetalleSeguimiento?> Consultar(Envio envio)
    {
        await using var contexto = Contexto();
        return await new ObtenerDetalleSeguimiento(
            new EnvioRepositorio(contexto),
            new BultoRepositorio(contexto),
            new HistorialEstadoRepositorio(contexto),
            new OperadorRepositorio(contexto),
            new RutaRepositorio(contexto),
            new ReglaOperativaRepositorio(contexto),
            _cache).PorTokenAsync(envio.TokenSeguimientoPublico);
    }

    private async Task<ResultadoReprogramacion> Reprogramar(string token)
    {
        await using var contexto = Contexto();
        var outbox = new OutboxRepositorio(contexto);
        var historial = new HistorialEstadoRepositorio(contexto);
        return await new SolicitarReprogramacion(
            new EnvioRepositorio(contexto),
            historial,
            new ReglaOperativaRepositorio(contexto),
            new CambiarEstadoEnvio(
                new EnvioRepositorio(contexto),
                new RutaRepositorio(contexto),
                new RegistrarOutboxEstado(outbox, historial),
                new PublicarOutboxPendientes(outbox, new PublicadorMensajesPrueba { Disponible = true }),
                _cache)).EjecutarAsync(token);
    }

    // Sin operador: el destinatario no tiene sesión.
    private PlataformaDbContext Contexto()
    {
        var operador = new OperadorActualFijo(null);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase(_db)
            .AddInterceptors(new OperadorSaveChangesInterceptor(operador))
            .Options;

        return new PlataformaDbContext(options, operador);
    }
}
