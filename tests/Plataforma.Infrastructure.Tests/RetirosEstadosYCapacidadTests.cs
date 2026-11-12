using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>RF 38 (estados del retiro) y RF 39 (capacidad del vehículo a lo largo de la ruta mixta).</summary>
public sealed class RetirosEstadosYCapacidadTests
{
    private static readonly Guid Operador = DatosSemilla.OperadorNorteId;
    private static readonly Guid Comercio = Guid.NewGuid();
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.UtcNow);

    private static SolicitudRetiro Retiro(decimal pesoKg = 0) =>
        SolicitudRetiro.Solicitar(Guid.NewGuid(), Operador, Comercio, "Calle 1", "", Hoy, pesoKg: pesoKg);

    [Fact]
    public void Ciclo_NoRetirado_NuevaVisita_Retirado_EnDeposito()
    {
        var retiro = Retiro();
        var ruta = Guid.NewGuid();
        retiro.ProgramarEnRuta(ruta, 2);
        retiro.MarcarEnCurso();
        retiro.NoRetirar("Cerrado");
        Assert.Equal((EstadoRetiro.NoRetirado, "Cerrado"), (retiro.Estado, retiro.MotivoNoRetiro));
        Assert.False(retiro.FueRetirado);

        // Nueva visita otro día: la fecha del retiro pasa a ser la de la ruta.
        retiro.ProgramarEnRuta(Guid.NewGuid(), null, Hoy.AddDays(1));
        Assert.Equal((EstadoRetiro.Programado, Hoy.AddDays(1), (int?)null), (retiro.Estado, retiro.FechaSolicitada, retiro.PosicionEnRuta));

        retiro.Completar();
        retiro.RecibirEnDeposito();
        Assert.Equal(EstadoRetiro.EnDeposito, retiro.Estado);
        Assert.True(retiro.FueRetirado);
        Assert.Throws<InvalidOperationException>(() => retiro.Cancelar("x"));
    }

    [Fact]
    public void TransicionesInvalidas_SeRechazan()
    {
        var retiro = Retiro();
        Assert.Throws<InvalidOperationException>(() => retiro.RecibirEnDeposito());
        Assert.Throws<InvalidOperationException>(() => retiro.NoRetirar("x"));
        Assert.Throws<InvalidOperationException>(() => retiro.MarcarEnCurso());
        Assert.Throws<InvalidOperationException>(() => retiro.ProgramarEnRuta(Guid.NewGuid(), null, Hoy.AddDays(1)));
        retiro.ProgramarEnRuta(Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => retiro.NoRetirar(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SolicitudRetiro.Solicitar(Guid.NewGuid(), Operador, Comercio, "C", "", Hoy, cantidadBultos: 0));
    }

    [Fact]
    public void Carga_BajaEnCadaEntrega_YSubeDesdeLaParadaDelRetiro()
    {
        var vehiculo = Vehiculo.Registrar(Guid.NewGuid(), Operador, "ABC 123", "Furgón", capacidadPesoKg: 100);
        EntregaEnRuta[] entregas = [new("E1", 40, 0), new("E2", 30, 0), new("E3", 20, 0)];

        // Sale con 90; después de 2 entregas quedan 20 y sube un retiro de 70: 90 de pico, entra.
        var entra = CapacidadRuta.Calcular(Guid.NewGuid(), vehiculo, entregas, [new RetiroEnRuta("R", 2, 70, 0)]);
        Assert.Equal((90m, 90m), (entra.CargaInicialPesoKg, entra.PicoPesoKg));
        Assert.Null(entra.Problema);
        Assert.Equal(["E1", "E2", "R", "E3"], entra.Paradas.Select(parada => parada.Descripcion));

        // El mismo retiro antes de la primera entrega: 160 kg, no entra.
        var noEntra = CapacidadRuta.Calcular(Guid.NewGuid(), vehiculo, entregas, [new RetiroEnRuta("R", 0, 70, 0)]);
        Assert.Equal(160m, noEntra.PicoPesoKg);
        Assert.Contains("160", noEntra.Problema);
    }

    [Fact]
    public async Task Programar_ValidaLaCapacidadDelVehiculoDeLaRuta()
    {
        await using var ctx = Crear();
        var vehiculo = Vehiculo.Registrar(Guid.NewGuid(), Operador, "ABC 123", "Furgón", capacidadPesoKg: 50);
        ctx.Vehiculos.Add(vehiculo);
        var ruta = Ruta.Programar(Operador, Guid.NewGuid(), Hoy);
        ruta.AsignarVehiculo(vehiculo.Id);
        ctx.Rutas.Add(ruta);
        var envio = Envio.Admitir(Operador, Comercio, "D", "C");
        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        envio.AsignarARuta(ruta.Id);
        ctx.Envios.Add(envio);
        ctx.Bultos.Add(Bulto.Crear(Guid.NewGuid(), Operador, envio.Id, "B-1", 40));
        ctx.Paradas.Add(Parada.Programar(Guid.NewGuid(), Operador, ruta.Id, envio.Id, 1));
        var pesado = Retiro(30);
        ctx.SolicitudesRetiro.Add(pesado);
        await ctx.SaveChangesAsync();
        var gestion = Gestion(ctx);

        // Antes de la entrega: 40 + 30 = 70 > 50.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gestion.ProgramarAsync(Operador, pesado.Id, ruta.Id, posicion: 0));
        Assert.Contains("no entra en el vehículo", error.Message);

        // Después de la entrega: 30, entra; y la ruta muestra el recorrido mixto.
        await using var otro = new PlataformaDbContext(ctx.Opciones, new OperadorActualFijo(Operador));
        await Gestion(otro).ProgramarAsync(Operador, pesado.Id, ruta.Id, posicion: 1);
        var perfil = await Gestion(otro).PerfilCargaAsync(Operador, ruta.Id);
        Assert.Equal((40m, 40m, 2), (perfil!.CargaInicialPesoKg, perfil.PicoPesoKg, perfil.Paradas.Count));

        Assert.Equal(1, await Gestion(otro).IniciarRutaAsync(Operador, ruta.Id));
        Assert.Equal(EstadoRetiro.EnCurso, (await new RetiroRepositorio(otro).ObtenerAsync(Operador, pesado.Id))!.Estado);
    }

    [Fact]
    public async Task Pedido_ConFranjaYMotivo_YLaFranjaTieneQueCoincidirConLaRuta()
    {
        await using var ctx = Crear();
        var comercio = Plataforma.Domain.Comercio.Registrar(Guid.NewGuid(), Operador, "Comercio", "Comercio SA", "211111110018", "Calle 1");
        ctx.Comercios.Add(comercio);
        var manana = FranjaHoraria.Definir(Guid.NewGuid(), Operador, "Mañana", new TimeOnly(0, 0), new TimeOnly(23, 59));
        var tarde = FranjaHoraria.Definir(Guid.NewGuid(), Operador, "Tarde", new TimeOnly(0, 0), new TimeOnly(23, 59));
        ctx.FranjasHorarias.AddRange(manana, tarde);
        var ruta = Ruta.Programar(Operador, Guid.NewGuid(), Hoy);
        ctx.Rutas.Add(ruta);
        var envio = Envio.Admitir(Operador, comercio.Id, "D", "C", franjaId: manana.Id);
        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        envio.AsignarARuta(ruta.Id);
        ctx.Envios.Add(envio);
        await ctx.SaveChangesAsync();
        var gestion = Gestion(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() => gestion.SolicitarAsync(
            Operador, comercio.Id, "Calle 1", "", Hoy, franjaId: Guid.NewGuid(), motivo: "Cambio de producto"));

        var retiro = await gestion.SolicitarAsync(
            Operador, comercio.Id, "Calle 1", "", Hoy, franjaId: tarde.Id, motivo: "Cambio de producto");
        Assert.Equal((tarde.Id, "Cambio de producto"), (retiro.FranjaId, retiro.Motivo));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => gestion.ProgramarAsync(Operador, retiro.Id, ruta.Id));
        Assert.Contains("otra franja", error.Message);
    }

    [Fact]
    public async Task App_RegistraRetiradoYNoRetirado_Idempotente_YValidaEvidencia()
    {
        await using var ctx = Crear();
        var repartidor = Guid.NewGuid();
        var ruta = Ruta.Programar(Operador, repartidor, Hoy);
        var otraRuta = Ruta.Programar(Operador, Guid.NewGuid(), Hoy);
        ctx.Rutas.AddRange(ruta, otraRuta);
        var uno = Retiro();
        var dos = Retiro();
        var ajeno = Retiro();
        uno.ProgramarEnRuta(ruta.Id);
        dos.ProgramarEnRuta(ruta.Id);
        ajeno.ProgramarEnRuta(otraRuta.Id);
        ctx.SolicitudesRetiro.AddRange(uno, dos, ajeno);
        await ctx.SaveChangesAsync();
        var registrar = new RegistrarRetirosRepartidor(
            new RutaRepositorio(ctx), new RetiroRepositorio(ctx), new ComprobanteRetiroRepositorio(ctx), Gestion(ctx), new CacheLecturaMemoria());
        const string Foto = "/9j/2Q==";
        const string Firma = "[[{\"x\":1,\"y\":1},{\"x\":9,\"y\":9}]]";
        var ahora = DateTimeOffset.UtcNow;
        var retirado = new ComprobanteRetiroApp(Guid.NewGuid(), uno.Id, "Retirado", ["CAMBIO-1", "CAMBIO-2"], Foto, Firma, "Ana", null, ahora, -34.9m, -56.16m);

        var resultado = await registrar.EjecutarAsync(Operador, repartidor, Hoy,
        [
            retirado,
            new ComprobanteRetiroApp(Guid.NewGuid(), dos.Id, "Retirado", ["X"], null, Firma, null, null, ahora, null, null),
            new ComprobanteRetiroApp(Guid.NewGuid(), ajeno.Id, "NoRetirado", null, null, null, null, "Cerrado", ahora, null, null)
        ], "alvaro");

        Assert.Equal(EstadoRetiro.Completado, Assert.Single(resultado.Aplicados).Estado);
        Assert.Contains(resultado.Rechazados, r => r.RetiroId == dos.Id && r.Motivo.Contains("foto"));
        Assert.Contains(resultado.Rechazados, r => r.RetiroId == ajeno.Id && r.Motivo.Contains("no está en tu ruta"));
        var guardado = Assert.Single(ctx.ComprobantesRetiro.AsNoTracking().ToList());
        Assert.Equal((uno.Id, "CAMBIO-1\nCAMBIO-2", "Ana"), (guardado.RetiroId, guardado.CodigosBultos, guardado.NombreEntrega));

        // Reenvío por respuesta perdida: mismo resultado, sin duplicar.
        var reenvio = await registrar.EjecutarAsync(Operador, repartidor, Hoy, [retirado], "alvaro");
        Assert.Equal(EstadoRetiro.Completado, Assert.Single(reenvio.Aplicados).Estado);
        Assert.Single(ctx.ComprobantesRetiro.AsNoTracking().ToList());

        var noRetirado = await registrar.EjecutarAsync(Operador, repartidor, Hoy,
            [new ComprobanteRetiroApp(Guid.NewGuid(), dos.Id, "NoRetirado", null, null, null, null, "No había nadie", ahora, null, null)], "alvaro");
        Assert.Equal(EstadoRetiro.NoRetirado, Assert.Single(noRetirado.Aplicados).Estado);
        Assert.Equal("No había nadie", (await new RetiroRepositorio(ctx).ObtenerAsync(Operador, dos.Id))!.MotivoNoRetiro);
    }

    private static GestionRetiros Gestion(PlataformaDbContext ctx) =>
        new(
            new RetiroRepositorio(ctx),
            new BanderasActivas(),
            new OutboxRepositorio(ctx),
            new PublicarOutboxPendientes(new OutboxRepositorio(ctx), new PublicadorMensajesPrueba { Disponible = false }),
            new ComercioRepositorio(ctx),
            rutas: new RutaRepositorio(ctx),
            envios: new EnvioRepositorio(ctx),
            bultos: new BultoRepositorio(ctx),
            cobertura: new CoberturaRepositorio(ctx));

    private static ContextoPrueba Crear()
    {
        var operadorActual = new OperadorActualFijo(Operador);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase($"retiros-capacidad-{Guid.NewGuid():N}")
            .AddInterceptors(new OperadorSaveChangesInterceptor(operadorActual))
            .Options;
        return new ContextoPrueba(options, operadorActual);
    }

    private sealed class ContextoPrueba(DbContextOptions<PlataformaDbContext> opciones, OperadorActualFijo operador)
        : PlataformaDbContext(opciones, operador)
    {
        public DbContextOptions<PlataformaDbContext> Opciones { get; } = opciones;
    }

    private sealed class BanderasActivas : IBanderasOperador
    {
        public Task<bool> EstaActivaAsync(Guid operadorId, string clave, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
