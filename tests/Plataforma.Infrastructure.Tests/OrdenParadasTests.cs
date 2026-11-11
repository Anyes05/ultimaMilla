using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>RF 16: criterio de orden de las paradas y hoja de ruta devuelta en ese orden.</summary>
public sealed class OrdenParadasTests
{
    private static readonly Guid Operador = DatosSemilla.OperadorNorteId;
    private static readonly Guid Comercio = Guid.NewGuid();

    [Fact]
    public void UrgentesPrimero_DespuesPorZona_YLasSinZonaAlFinal()
    {
        var centro = Guid.NewGuid();
        var este = Guid.NewGuid();
        var zonas = new Dictionary<Guid, string> { [centro] = "Centro", [este] = "Este" };
        var a = Envio.Admitir(Operador, Comercio, "A", "C", zonaId: este);
        var b = Envio.Admitir(Operador, Comercio, "B", "C");
        var c = Envio.Admitir(Operador, Comercio, "C", "C", zonaId: centro);
        var d = Envio.Admitir(Operador, Comercio, "D", "C", zonaId: este, urgente: true);
        var e = Envio.Admitir(Operador, Comercio, "E", "C", zonaId: centro);

        var orden = DespacharRuta.OrdenDeParadas([a, b, c, d, e], zonas).Select(envio => envio.Destinatario);

        Assert.Equal(["D", "C", "E", "A", "B"], orden);
    }

    [Fact]
    public async Task ObtenerPorRuta_DevuelveEnOrdenDeParada()
    {
        var operadorActual = new OperadorActualFijo(Operador);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase($"orden-paradas-{Guid.NewGuid():N}")
            .AddInterceptors(new OperadorSaveChangesInterceptor(operadorActual))
            .Options;
        await using var ctx = new PlataformaDbContext(options, operadorActual);
        var ruta = Ruta.Programar(Operador, Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));
        ctx.Rutas.Add(ruta);
        var envios = new[] { "Primero", "Segundo", "Tercero" }
            .Select(nombre => Envio.Admitir(Operador, Comercio, nombre, "C"))
            .ToList();
        foreach (var envio in envios)
        {
            envio.CambiarEstado(EstadoEnvio.EnDeposito);
            envio.AsignarARuta(ruta.Id);
        }
        // Se guardan en otro orden que el de visita.
        ctx.Envios.AddRange(envios[2], envios[0], envios[1]);
        ctx.Paradas.AddRange(
            Parada.Programar(Guid.NewGuid(), Operador, ruta.Id, envios[1].Id, 2),
            Parada.Programar(Guid.NewGuid(), Operador, ruta.Id, envios[2].Id, 3),
            Parada.Programar(Guid.NewGuid(), Operador, ruta.Id, envios[0].Id, 1));
        await ctx.SaveChangesAsync();

        var lista = await new EnvioRepositorio(ctx).ObtenerPorRutaAsync(Operador, ruta.Id);

        Assert.Equal(["Primero", "Segundo", "Tercero"], lista.Select(envio => envio.Destinatario));
    }
}
