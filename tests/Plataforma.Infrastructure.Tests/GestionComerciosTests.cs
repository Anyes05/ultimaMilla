using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>ABM de comercios (RF 1): por operador, sin documentos repetidos y con baja lógica.</summary>
public sealed class GestionComerciosTests
{
    private readonly string _db = $"comercios-{Guid.NewGuid():N}";

    [Fact]
    public async Task Crear_LoListaSoloEnSuOperador()
    {
        await Con(DatosSemilla.OperadorNorteId, servicio => Alta(servicio, DatosSemilla.OperadorNorteId));

        await using var contexto = Contexto(null);
        var repositorio = new ComercioRepositorio(contexto);
        var comercio = Assert.Single(await repositorio.ListarAsync(DatosSemilla.OperadorNorteId));
        Assert.True(comercio.Activo);
        Assert.Empty(await repositorio.ListarAsync(DatosSemilla.OperadorSurId));
    }

    [Fact]
    public async Task DocumentoRepetido_SeRechazaEnElMismoOperador()
    {
        await Con(DatosSemilla.OperadorNorteId, servicio => Alta(servicio, DatosSemilla.OperadorNorteId));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Con(DatosSemilla.OperadorNorteId, servicio => Alta(servicio, DatosSemilla.OperadorNorteId, "Otra")));
    }

    [Fact]
    public async Task MismoDocumento_SePermiteEnOtroOperador()
    {
        await Con(DatosSemilla.OperadorNorteId, servicio => Alta(servicio, DatosSemilla.OperadorNorteId));

        await Con(DatosSemilla.OperadorSurId, servicio => Alta(servicio, DatosSemilla.OperadorSurId));
    }

    [Fact]
    public async Task Modificar_ConservaSuPropioDocumento()
    {
        var id = (await Con(DatosSemilla.OperadorNorteId, servicio => Alta(servicio, DatosSemilla.OperadorNorteId))).Id;

        await Con<object?>(DatosSemilla.OperadorNorteId, async servicio =>
        {
            await servicio.ActualizarAsync(DatosSemilla.OperadorNorteId, id, "Renombrada", "Tienda Uno SA", "210000000011", "Calle 1");
            return null;
        });

        await using var contexto = Contexto(null);
        Assert.Equal("Renombrada", (await contexto.Comercios.SingleAsync()).NombreComercial);
    }

    [Fact]
    public async Task OtroOperador_NoPuedeModificarNiDarDeBaja()
    {
        var id = (await Con(DatosSemilla.OperadorNorteId, servicio => Alta(servicio, DatosSemilla.OperadorNorteId))).Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Con<object?>(DatosSemilla.OperadorSurId, async servicio =>
        {
            await servicio.CambiarActivoAsync(DatosSemilla.OperadorSurId, id, false);
            return null;
        }));
    }

    [Fact]
    public async Task Baja_EsLogicaYSeRevierte()
    {
        var id = (await Con(DatosSemilla.OperadorNorteId, servicio => Alta(servicio, DatosSemilla.OperadorNorteId))).Id;

        await CambiarActivo(id, false);
        await using (var contexto = Contexto(null))
            Assert.False(await new ComercioRepositorio(contexto).EstaActivoAsync(id));

        await CambiarActivo(id, true);
        await using (var contexto = Contexto(null))
            Assert.True(await new ComercioRepositorio(contexto).EstaActivoAsync(id));
    }

    private static Task<Comercio> Alta(GestionComercios servicio, Guid operadorId, string nombre = "Tienda Uno") =>
        servicio.RegistrarAsync(operadorId, nombre, "Tienda Uno SA", "210000000011", "Calle 1");

    private Task CambiarActivo(Guid id, bool activo) => Con<object?>(DatosSemilla.OperadorNorteId, async servicio =>
    {
        await servicio.CambiarActivoAsync(DatosSemilla.OperadorNorteId, id, activo);
        return null;
    });

    private async Task<T> Con<T>(Guid operadorId, Func<GestionComercios, Task<T>> accion)
    {
        await using var contexto = Contexto(operadorId);
        return await accion(new GestionComercios(new ComercioRepositorio(contexto)));
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
