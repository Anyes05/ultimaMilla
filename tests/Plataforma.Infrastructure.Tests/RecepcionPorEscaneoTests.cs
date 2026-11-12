using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>RF 10: recepción por escaneo de bultos, con registro de los faltantes.</summary>
public sealed class RecepcionPorEscaneoTests
{
    private static readonly Guid Operador = DatosSemilla.OperadorNorteId;

    [Fact]
    public async Task Escaneo_RecibeCadaEnvio_RegistraFaltantes_YAvisaLoQueNoEsPendiente()
    {
        var db = $"recepcion-escaneo-{Guid.NewGuid():N}";
        Envio dos, uno, sinBultos;
        await using (var semilla = Crear(db, null))
        {
            semilla.Operadores.Add(Plataforma.Domain.Operador.Crear(Operador, "Norte", "azul", 3));
            dos = Envio.Admitir(Operador, DatosSemilla.ComercioNorteId, "Dos bultos", "Calle 1");
            uno = Envio.Admitir(Operador, DatosSemilla.ComercioNorteId, "Un bulto", "Calle 2");
            sinBultos = Envio.Admitir(Operador, DatosSemilla.ComercioNorteId, "Sin bultos", "Calle 3");
            semilla.Envios.AddRange(dos, uno, sinBultos);
            semilla.Bultos.AddRange(
                Bulto.Crear(Guid.NewGuid(), Operador, dos.Id, "DOS-B01", 1),
                Bulto.Crear(Guid.NewGuid(), Operador, dos.Id, "DOS-B02", 1),
                Bulto.Crear(Guid.NewGuid(), Operador, uno.Id, "UNO-B01", 1));
            await semilla.SaveChangesAsync();
        }

        await using var contexto = Crear(db, Operador);
        var recibir = Recibir(contexto);

        // Llega solo un bulto de "Dos", el de "Uno" (en minúsculas), "Sin bultos" por su número y un código ajeno.
        var resultado = await recibir.RecibirPorEscaneoAsync(
            Operador, ["DOS-B01", "uno-b01", sinBultos.NumeroSeguimiento, "XYZ-999", "DOS-B01"], "operario.norte");

        Assert.Equal(["XYZ-999"], resultado.NoReconocidos);
        Assert.Empty(resultado.Errores);
        var conFaltante = Assert.Single(resultado.Recibidos, r => r.NumeroSeguimiento == dos.NumeroSeguimiento);
        Assert.Equal((2, 1), (conFaltante.BultosEsperados, conFaltante.BultosRecibidos));
        Assert.Equal(["DOS-B02"], conFaltante.Faltantes);
        Assert.Empty(Assert.Single(resultado.Recibidos, r => r.NumeroSeguimiento == uno.NumeroSeguimiento).Faltantes);
        Assert.Contains(resultado.Recibidos, r => r.NumeroSeguimiento == sinBultos.NumeroSeguimiento);

        var estados = await contexto.Envios.AsNoTracking().Select(envio => envio.Estado).Distinct().ToListAsync();
        Assert.Equal([EstadoEnvio.EnDeposito], estados);

        var discrepancia = Assert.Single(await new RecepcionRepositorio(contexto).ListarDiscrepanciasAsync(Operador, 10));
        Assert.Equal((dos.NumeroSeguimiento, "DOS-B02"), (discrepancia.NumeroSeguimiento, discrepancia.BultosFaltantes));
        Assert.Contains("Faltan: DOS-B02", discrepancia.Observaciones);

        // Volver a escanear un bulto de un envío ya recibido no lo recibe otra vez.
        var repetido = await recibir.RecibirPorEscaneoAsync(Operador, ["DOS-B02"], "operario.norte");
        Assert.Empty(repetido.Recibidos);
        Assert.Contains("ya fue recibido", Assert.Single(repetido.NoReconocidos));
    }

    [Fact]
    public async Task Escaneo_SinCodigos_SeRechaza()
    {
        await using var contexto = Crear($"recepcion-vacia-{Guid.NewGuid():N}", Operador);
        await Assert.ThrowsAsync<ArgumentException>(() => Recibir(contexto).RecibirPorEscaneoAsync(Operador, [" ", ""], "operario"));
    }

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
