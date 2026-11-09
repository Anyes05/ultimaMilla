using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>Reglas operativas (RF 4, letra 6.6): se cambian en caliente, por operador y conservando la versión anterior.</summary>
public sealed class GestionReglasOperativasTests
{
    private static readonly NuevaVersionRegla Nueva = new(5, 72, 6, false, true, true, PoliticaBultosPendientes.DevolucionAlComercio);

    private readonly string _db = $"reglas-{Guid.NewGuid():N}";
    private readonly CacheLecturaMemoria _cache = new();

    [Fact]
    public async Task Publicar_CreaVersionNuevaYConservaLaAnteriorParaSuPeriodo()
    {
        var antes = DateTime.UtcNow.AddDays(-1);
        await Sembrar();

        await Publicar(DatosSemilla.OperadorNorteId, Nueva);

        await using var contexto = Contexto(DatosSemilla.OperadorNorteId);
        var reglas = new ReglaOperativaRepositorio(contexto);
        Assert.Equal(2, (await reglas.ListarVersionesAsync(DatosSemilla.OperadorNorteId)).Count);
        Assert.Equal(5, (await reglas.ObtenerVigenteAsync(DatosSemilla.OperadorNorteId, DateTime.UtcNow))!.MaximoIntentos);
        Assert.Equal(3, (await reglas.ObtenerVigenteAsync(DatosSemilla.OperadorNorteId, antes))!.MaximoIntentos);
    }

    [Fact]
    public async Task Publicar_ActualizaElMaximoQueValidaLaSincronizacion()
    {
        await Sembrar();

        await Publicar(DatosSemilla.OperadorNorteId, Nueva);

        await using var contexto = Contexto(DatosSemilla.OperadorNorteId);
        Assert.Equal(5, (await contexto.Operadores.SingleAsync(item => item.Id == DatosSemilla.OperadorNorteId)).MaximoIntentos);
    }

    [Fact]
    public async Task Publicar_NoAfectaAOtroOperador()
    {
        await Sembrar();

        await Publicar(DatosSemilla.OperadorNorteId, Nueva);

        await using var contexto = Contexto(null);
        var reglas = new ReglaOperativaRepositorio(contexto);
        Assert.Equal(1, (await reglas.ObtenerVigenteAsync(DatosSemilla.OperadorSurId, DateTime.UtcNow))!.MaximoIntentos);
        Assert.Single(await reglas.ListarVersionesAsync(DatosSemilla.OperadorSurId));
        Assert.Equal(1, (await contexto.Operadores.SingleAsync(item => item.Id == DatosSemilla.OperadorSurId)).MaximoIntentos);
    }

    [Fact]
    public async Task Publicar_ValoresInvalidos_NoGuardaNada()
    {
        await Sembrar();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Publicar(DatosSemilla.OperadorNorteId, Nueva with { PlazoEntregaHoras = 0 }));

        await using var contexto = Contexto(DatosSemilla.OperadorNorteId);
        Assert.Single(await new ReglaOperativaRepositorio(contexto).ListarVersionesAsync(DatosSemilla.OperadorNorteId));
    }

    private async Task Sembrar()
    {
        await using var contexto = Contexto(null);
        var desde = DateTime.UtcNow.AddYears(-1);
        var hasta = DateTime.UtcNow.AddYears(1);
        contexto.Operadores.AddRange(
            Operador.Crear(DatosSemilla.OperadorNorteId, "Operador Norte", "azul", 3),
            Operador.Crear(DatosSemilla.OperadorSurId, "Operador Sur", "verde", 1));
        contexto.ReglasOperativas.AddRange(
            ReglaOperativa.Crear(Guid.NewGuid(), DatosSemilla.OperadorNorteId, 3, 48, true, true, false, 24, desde, hasta),
            ReglaOperativa.Crear(Guid.NewGuid(), DatosSemilla.OperadorSurId, 1, 24, false, true, false, 12, desde, hasta));
        await contexto.SaveChangesAsync();
    }

    private async Task Publicar(Guid operadorId, NuevaVersionRegla datos)
    {
        await using var contexto = Contexto(operadorId);
        await new GestionReglasOperativas(new ReglaOperativaRepositorio(contexto), new OperadorRepositorio(contexto), _cache)
            .PublicarAsync(operadorId, datos);
    }

    private PlataformaDbContext Contexto(Guid? operadorId)
    {
        var operador = new OperadorActualFijo(operadorId);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase(_db)
            .AddInterceptors(new OperadorSaveChangesInterceptor(operador))
            .Options;

        return new PlataformaDbContext(options, operador);
    }
}
