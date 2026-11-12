using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>RF 10: recepción por escaneo de bultos (de a uno), con discrepancia solo si el operario cierra con faltantes.</summary>
public sealed class RecepcionPorEscaneoTests
{
    private static readonly Guid Operador = DatosSemilla.OperadorNorteId;

    [Fact]
    public async Task EscaneoDeAUno_RecibeAlLlegarElUltimoBulto_SinDiscrepancia()
    {
        var db = $"recepcion-escaneo-{Guid.NewGuid():N}";
        var (tres, uno, sinBultos) = await SembrarAsync(db);
        await using var contexto = Crear(db, Operador);
        var recibir = Recibir(contexto);

        // Primer bulto: el envío sigue pendiente, "en recepción", sin discrepancia.
        var primero = await recibir.RecibirPorEscaneoAsync(Operador, ["TRES-B01"], "operario");
        Assert.Empty(primero.Recibidos);
        var curso = Assert.Single(primero.EnCurso!);
        Assert.Equal((1, 3), (curso.Escaneados, curso.Esperados));
        Assert.Equal(["TRES-B02", "TRES-B03"], curso.Faltantes);
        Assert.Equal(EstadoEnvio.Admitido, await EstadoAsync(contexto, tres.Id));

        // Repetir un escaneo no cuenta dos veces.
        var repetido = await recibir.RecibirPorEscaneoAsync(Operador, ["TRES-B01"], "operario");
        Assert.Contains("ya estaba escaneado", Assert.Single(repetido.NoReconocidos));

        // Segundo (en minúsculas) y tercero por separado: con el último se recibe completo.
        await recibir.RecibirPorEscaneoAsync(Operador, ["tres-b02"], "operario");
        var ultimo = await recibir.RecibirPorEscaneoAsync(Operador, ["TRES-B03"], "operario");
        var recibido = Assert.Single(ultimo.Recibidos);
        Assert.Equal((3, 3), (recibido.BultosEsperados, recibido.BultosRecibidos));
        Assert.Equal(EstadoEnvio.EnDeposito, await EstadoAsync(contexto, tres.Id));

        // En la misma tanda: un envío de un bulto, uno sin bultos por su número y un código ajeno.
        var tanda = await recibir.RecibirPorEscaneoAsync(Operador, ["UNO-B01", sinBultos.NumeroSeguimiento, "XYZ-999"], "operario");
        Assert.Equal(2, tanda.Recibidos.Count);
        Assert.Equal(["XYZ-999"], tanda.NoReconocidos);
        Assert.Equal(EstadoEnvio.EnDeposito, await EstadoAsync(contexto, uno.Id));

        Assert.Empty(await new RecepcionRepositorio(contexto).ListarDiscrepanciasAsync(Operador, 10));
    }

    [Fact]
    public async Task CerrarConFaltantes_RegistraLaDiscrepancia_YLosQueLleganDespuesLaCompletan()
    {
        var db = $"recepcion-faltantes-{Guid.NewGuid():N}";
        var (tres, _, _) = await SembrarAsync(db);
        await using var contexto = Crear(db, Operador);
        var recibir = Recibir(contexto);

        await recibir.RecibirPorEscaneoAsync(Operador, ["TRES-B01"], "operario");
        var cerrado = await recibir.CerrarConFaltantesAsync(Operador, tres.Id, "operario", "caja rota");
        Assert.Equal((3, 1), (cerrado.BultosEsperados, cerrado.BultosRecibidos));
        Assert.Equal(["TRES-B02", "TRES-B03"], cerrado.Faltantes);
        Assert.Equal(EstadoEnvio.EnDeposito, await EstadoAsync(contexto, tres.Id));

        var discrepancia = Assert.Single(await new RecepcionRepositorio(contexto).ListarDiscrepanciasAsync(Operador, 10));
        Assert.Equal("TRES-B02\nTRES-B03", discrepancia.BultosFaltantes);
        Assert.Contains("caja rota (faltan: TRES-B02, TRES-B03)", discrepancia.Observaciones);
        Assert.False(discrepancia.Completada);

        var segundo = await recibir.RecibirPorEscaneoAsync(Operador, ["TRES-B02"], "operario");
        Assert.False(Assert.Single(segundo.LlegadasPosteriores!).Completa);
        var tercero = await recibir.RecibirPorEscaneoAsync(Operador, ["TRES-B03"], "operario");
        Assert.True(Assert.Single(tercero.LlegadasPosteriores!).Completa);

        var completa = Assert.Single(await new RecepcionRepositorio(contexto).ListarDiscrepanciasAsync(Operador, 10));
        Assert.Equal((3, true, ""), (completa.BultosRecibidos, completa.Completada, completa.BultosFaltantes));
        Assert.Contains("Llegó después: TRES-B03", completa.Observaciones);

        // Ya completo: un nuevo escaneo de ese envío no se cuenta.
        Assert.Contains("ya fue recibido", Assert.Single((await recibir.RecibirPorEscaneoAsync(Operador, ["TRES-B03"], "operario")).NoReconocidos));
    }

    [Fact]
    public async Task Escaneo_SinCodigos_SeRechaza()
    {
        await using var contexto = Crear($"recepcion-vacia-{Guid.NewGuid():N}", Operador);
        await Assert.ThrowsAsync<ArgumentException>(() => Recibir(contexto).RecibirPorEscaneoAsync(Operador, [" ", ""], "operario"));
    }

    private static async Task<(Envio Tres, Envio Uno, Envio SinBultos)> SembrarAsync(string db)
    {
        await using var semilla = Crear(db, null);
        semilla.Operadores.Add(Plataforma.Domain.Operador.Crear(Operador, "Norte", "azul", 3));
        var tres = Envio.Admitir(Operador, DatosSemilla.ComercioNorteId, "Tres bultos", "Calle 1");
        var uno = Envio.Admitir(Operador, DatosSemilla.ComercioNorteId, "Un bulto", "Calle 2");
        var sinBultos = Envio.Admitir(Operador, DatosSemilla.ComercioNorteId, "Sin bultos", "Calle 3");
        semilla.Envios.AddRange(tres, uno, sinBultos);
        semilla.Bultos.AddRange(
            Bulto.Crear(Guid.NewGuid(), Operador, tres.Id, "TRES-B01", 1),
            Bulto.Crear(Guid.NewGuid(), Operador, tres.Id, "TRES-B02", 1),
            Bulto.Crear(Guid.NewGuid(), Operador, tres.Id, "TRES-B03", 1),
            Bulto.Crear(Guid.NewGuid(), Operador, uno.Id, "UNO-B01", 1));
        await semilla.SaveChangesAsync();
        return (tres, uno, sinBultos);
    }

    private static Task<EstadoEnvio> EstadoAsync(PlataformaDbContext contexto, Guid envioId) =>
        contexto.Envios.AsNoTracking().Where(envio => envio.Id == envioId).Select(envio => envio.Estado).SingleAsync();

    private static RecibirEnvioEnDeposito Recibir(PlataformaDbContext contexto)
    {
        var outbox = new OutboxRepositorio(contexto);
        return new RecibirEnvioEnDeposito(
            new EnvioRepositorio(contexto),
            new BultoRepositorio(contexto),
            new RecepcionRepositorio(contexto),
            new CambiarEstadoEnvio(
                new EnvioRepositorio(contexto),
                new RutaRepositorio(contexto),
                new RegistrarOutboxEstado(outbox, new HistorialEstadoRepositorio(contexto)),
                new PublicarOutboxPendientes(outbox, new PublicadorMensajesPrueba { Disponible = false }),
                new CacheLecturaMemoria()));
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
