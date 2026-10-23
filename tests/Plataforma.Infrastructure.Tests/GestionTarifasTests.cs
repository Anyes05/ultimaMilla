using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class GestionTarifasTests
{
    [Fact]
    public async Task PublicarVersion_CierraLaAnteriorYListaHistorial()
    {
        var db = $"tarifas-{Guid.NewGuid():N}";
        await using var contexto = Crear(db);
        contexto.Operadores.Add(Operador.Crear(
            DatosSemilla.OperadorNorteId,
            DatosSemilla.NorteNombre,
            DatosSemilla.NorteColor,
            DatosSemilla.NorteMaximoIntentos));

        var regla = ReglaTarifaria.Crear(
            DatosSemilla.ReglaTarifaNorteId,
            DatosSemilla.OperadorNorteId,
            100, 10, 5, 20, 30);
        var desde = DateTime.UtcNow.AddDays(-10);
        contexto.ReglasTarifarias.Add(regla);
        contexto.VigenciasTarifa.Add(VigenciaTarifa.Crear(
            DatosSemilla.VigenciaTarifaNorteId,
            DatosSemilla.OperadorNorteId,
            regla.Id,
            desde,
            GestionTarifas.VigenciaAbiertaHasta));
        await contexto.SaveChangesAsync();

        var gestion = new GestionTarifas(new TarifaRepositorio(contexto));
        var nuevoDesde = DateTime.UtcNow.AddMinutes(1);
        var id = await gestion.PublicarVersionAsync(
            DatosSemilla.OperadorNorteId,
            new NuevaVersionTarifa(120, 12, 6, 25, 35),
            nuevoDesde,
            "norte.admin");

        var versiones = await gestion.ListarVersionesAsync(DatosSemilla.OperadorNorteId);
        Assert.Equal(2, versiones.Count);
        Assert.Contains(versiones, v => v.Id == id && v.Valores.PrecioBase == 120);
        Assert.Contains(versiones, v => v.Id == regla.Id && v.Hasta < GestionTarifas.VigenciaAbiertaHasta);
    }

    private static PlataformaDbContext Crear(string nombre)
    {
        var operador = new OperadorActualFijo(null);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase(nombre)
            .AddInterceptors(new OperadorSaveChangesInterceptor(operador))
            .Options;
        return new PlataformaDbContext(options, operador);
    }
}
