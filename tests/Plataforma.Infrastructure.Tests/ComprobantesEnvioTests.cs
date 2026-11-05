using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>Comprobantes en el Backoffice (RF 20): se leen completos y nunca cruzan de operador.</summary>
public sealed class ComprobantesEnvioTests
{
    private static readonly AccesoActual Norte = new(Guid.NewGuid(), DatosSemilla.OperadorNorteId, null, Roles.Administrador);

    [Fact]
    public async Task DevuelveFotoFirmaYUbicacionDelEnvio()
    {
        var db = NombreDb();
        var envio = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "Ana Norte", "Calle Norte 1");
        var fecha = new DateTimeOffset(2026, 11, 5, 10, 30, 0, TimeSpan.Zero);
        await Sembrar(db, envio, Prueba(envio, fecha, "/9j/2Q==", """[[{"x":10,"y":20},{"x":30,"y":40}]]"""));

        var resultado = await Consultar(db, Norte, envio.Id);

        var comprobante = Assert.Single(resultado!.Comprobantes);
        Assert.Equal("/9j/2Q==", comprobante.FotoBase64);
        Assert.Equal([new PuntoFirma(10, 20), new PuntoFirma(30, 40)], Assert.Single(comprobante.Firma));
        Assert.Equal(-34.9m, comprobante.Latitud);
        Assert.Equal(-56.2m, comprobante.Longitud);
        Assert.Equal(fecha, comprobante.FechaDispositivo);
    }

    [Fact]
    public async Task EnvioDeOtroOperador_NoSeDevuelve()
    {
        var db = NombreDb();
        var envioSur = Envio.Admitir(DatosSemilla.OperadorSurId, DatosSemilla.ComercioSurId, "Ana Sur", "Calle Sur 1");
        await Sembrar(db, envioSur, Prueba(envioSur, DateTimeOffset.UtcNow, "/9j/2Q==", ""));

        Assert.Null(await Consultar(db, Norte, envioSur.Id));
    }

    [Fact]
    public async Task FotoOFirmaInvalidas_NoRompenLaPantalla()
    {
        var db = NombreDb();
        var envio = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "Ana Norte", "Calle Norte 1");
        await Sembrar(db, envio, Prueba(envio, DateTimeOffset.UtcNow, "no es base64 <script>", "{firma rota"));

        var comprobante = Assert.Single((await Consultar(db, Norte, envio.Id))!.Comprobantes);

        Assert.Null(comprobante.FotoBase64);
        Assert.Empty(comprobante.Firma);
    }

    private static PruebaDeEntrega Prueba(Envio envio, DateTimeOffset fecha, string foto, string firma) =>
        PruebaDeEntrega.DesdeSincronizacion(Guid.NewGuid(), envio.OperadorId, envio.Id, "Receptor", "Puerta",
            "alvaro@demo.test", fecha, foto, firma, -34.9m, -56.2m);

    private static async Task Sembrar(string db, Envio envio, PruebaDeEntrega prueba)
    {
        await using var contexto = CrearContexto(db, operadorId: null);
        contexto.Envios.Add(envio);
        contexto.PruebasDeEntrega.Add(prueba);
        await contexto.SaveChangesAsync();
    }

    private static async Task<ComprobantesEnvio?> Consultar(string db, AccesoActual acceso, Guid envioId)
    {
        await using var contexto = CrearContexto(db, acceso.OperadorId);
        var casoDeUso = new ObtenerComprobantesEnvio(new EnvioRepositorio(contexto), new PruebaDeEntregaRepositorio(contexto));
        return await casoDeUso.Ejecutar(acceso, envioId);
    }

    private static string NombreDb() => $"comprobantes-{Guid.NewGuid():N}";

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
