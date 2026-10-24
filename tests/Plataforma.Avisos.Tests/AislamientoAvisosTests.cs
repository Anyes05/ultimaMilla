using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Avisos;

namespace Plataforma.Avisos.Tests;

/// <summary>Un operador no ve ni reintenta avisos de otro, y un comercio no ve los de otro comercio.</summary>
public class AislamientoAvisosTests
{
    private static readonly Guid Norte = Guid.NewGuid();
    private static readonly Guid Sur = Guid.NewGuid();
    private static readonly Guid ComercioNorte = Guid.NewGuid();
    private static readonly Guid OtroComercioNorte = Guid.NewGuid();
    private static readonly Guid ComercioSur = Guid.NewGuid();

    [Fact]
    public async Task Operador_SoloVeSusFallidos()
    {
        await using var db = Crear();
        await SembrarAsync(db);
        var servicio = Servicio(db, new PublicadorFalso());

        var fallidosNorte = await servicio.ListarEntregasOperadorAsync(Norte, EstadoEntrega.Fallido);
        var fallidosSur = await servicio.ListarEntregasOperadorAsync(Sur, EstadoEntrega.Fallido);

        Assert.Equal(2, fallidosNorte.Count);
        Assert.Single(fallidosSur);
        Assert.DoesNotContain(fallidosNorte, entrega => entrega.ComercioId == ComercioSur);
    }

    [Fact]
    public async Task Comercio_SoloVeSuHistorial()
    {
        await using var db = Crear();
        await SembrarAsync(db);
        var servicio = Servicio(db, new PublicadorFalso());

        var historial = await servicio.ListarEntregasComercioAsync(Norte, ComercioNorte);

        Assert.Single(historial);
        Assert.All(historial, entrega => Assert.Equal(ComercioNorte, entrega.ComercioId));
    }

    [Fact]
    public async Task Operador_NoPuedeReintentarAvisoDeOtro()
    {
        await using var db = Crear();
        var entregasSur = await SembrarAsync(db);
        var publicador = new PublicadorFalso();
        var servicio = Servicio(db, publicador);

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ReintentarAsync(entregasSur, Norte));
        Assert.Empty(publicador.Publicadas);
    }

    [Fact]
    public async Task Comercio_NoPuedeReintentarAvisoDeOtroComercio()
    {
        await using var db = Crear();
        await SembrarAsync(db);
        var publicador = new PublicadorFalso();
        var servicio = Servicio(db, publicador);
        var deOtroComercio = (await servicio.ListarEntregasComercioAsync(Norte, OtroComercioNorte)).Single();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ReintentarAsync(deOtroComercio.Id, Norte, ComercioNorte));
        Assert.Empty(publicador.Publicadas);
    }

    [Fact]
    public async Task Reintento_VuelvePendienteYEncolaConElMismoEventId()
    {
        await using var db = Crear();
        await SembrarAsync(db);
        var publicador = new PublicadorFalso();
        var servicio = Servicio(db, publicador);
        var fallida = (await servicio.ListarEntregasComercioAsync(Norte, ComercioNorte)).Single();

        await servicio.ReintentarAsync(fallida.Id, Norte, ComercioNorte);

        var actual = await db.Entregas.AsNoTracking().SingleAsync(e => e.Id == fallida.Id);
        Assert.Equal(EstadoEntrega.Pendiente, actual.Estado);
        Assert.Equal(fallida.EventId, actual.EventId);
        Assert.Equal([fallida.Id], publicador.Publicadas);
    }

    private static AvisosDbContext Crear() =>
        new(new DbContextOptionsBuilder<AvisosDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ServicioAvisos Servicio(AvisosDbContext db, IPublicadorAvisos publicador) =>
        new(db, publicador, Options.Create(new OpcionesAvisos()));

    /// <summary>Siembra un fallido por comercio (2 en Norte, 1 en Sur) y devuelve el id del de Sur.</summary>
    private static async Task<Guid> SembrarAsync(AvisosDbContext db)
    {
        var ahora = DateTime.UtcNow;
        Guid idSur = Guid.Empty;
        foreach (var (operador, comercio) in new[] { (Norte, ComercioNorte), (Norte, OtroComercioNorte), (Sur, ComercioSur) })
        {
            var suscripcion = SuscripcionAviso.Crear(operador, comercio, "https://comercio.test/avisos", "secreto-de-prueba-123456", [], true, ahora);
            var entrega = EntregaAviso.Crear(Guid.NewGuid(), suscripcion, Guid.NewGuid(), "Entregado", "{}", ahora);
            entrega.RegistrarFallo("caído", 503, maximoReintentos: 0, ahora);
            db.Suscripciones.Add(suscripcion);
            db.Entregas.Add(entrega);
            if (operador == Sur)
                idSur = entrega.Id;
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return idSur;
    }

    private sealed class PublicadorFalso : IPublicadorAvisos
    {
        public List<Guid> Publicadas { get; } = [];

        public Task PublicarEntregaAsync(Guid entregaId, CancellationToken cancellationToken = default)
        {
            Publicadas.Add(entregaId);
            return Task.CompletedTask;
        }

        public Task PublicarResultadoAsync(ResultadoAviso resultado, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
