using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Avisos;

namespace Plataforma.Avisos.Tests;

/// <summary>RF 27: correo con marca y enlace, reintentos con límite y reintento manual aislado por operador.</summary>
public class NotificacionDestinatarioTests
{
    private static readonly Guid Norte = Guid.NewGuid();
    private static readonly Guid Sur = Guid.NewGuid();
    private static readonly DateTime Ahora = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Admitido", true)]
    [InlineData("EnDeposito", true)]
    [InlineData("EnTransito", true)]
    [InlineData("Entregado", true)]
    [InlineData("NoEntregado", true)]
    [InlineData("Reprogramado", true)]
    [InlineData("AsignadoARuta", false)]
    public void SoloLosEstadosRelevantesGeneranCorreo(string estado, bool relevante) =>
        Assert.Equal(relevante, PlantillaNotificacion.EsRelevante(estado));

    [Fact]
    public void Correo_LlevaMarcaYEnlacePublico()
    {
        var correo = PlantillaNotificacion.Armar(
            new DatosNotificacion("Operador Norte", "azul", "ENV-123", "token123", "EnTransito"), "https://tracking.test/");

        Assert.Equal("Operador Norte: Tu envío está en camino", correo.Asunto);
        Assert.Contains("https://tracking.test/seguimiento/token123", correo.Html);
        Assert.Contains("https://tracking.test/seguimiento/token123", correo.Texto);
        Assert.Contains("#0d6efd", correo.Html);
    }

    [Fact]
    public void Correo_EscapaElNombreDelOperador()
    {
        var correo = PlantillaNotificacion.Armar(
            new DatosNotificacion("<script>x</script>", "azul", "ENV-1", "t", "Entregado"), "https://tracking.test");

        Assert.DoesNotContain("<script>", correo.Html);
    }

    [Fact]
    public void AgotadosLosReintentos_QuedaFallida()
    {
        var notificacion = Crear(Norte);

        Assert.True(notificacion.RegistrarFallo("error", 3, Ahora));
        Assert.True(notificacion.RegistrarFallo("error", 3, Ahora));
        Assert.True(notificacion.RegistrarFallo("error", 3, Ahora));
        Assert.False(notificacion.RegistrarFallo("error", 3, Ahora));
        Assert.Equal(EstadoNotificacionDestinatario.Fallida, notificacion.Estado);
    }

    [Fact]
    public async Task Fallidas_SoloSeVenYReintentanDentroDelOperador()
    {
        await using var db = Db();
        var deSur = Fallida(Sur);
        db.NotificacionesDestinatario.AddRange(Fallida(Norte), deSur);
        await db.SaveChangesAsync();
        var publicador = new PublicadorFalso();
        var servicio = Servicio(db, publicador);

        Assert.Single(await servicio.ListarNotificacionesFallidasAsync(Norte));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ReintentarNotificacionAsync(deSur.Id, Norte));
        Assert.Empty(publicador.Publicadas);
    }

    [Fact]
    public async Task Reintento_ReenviaElEventoOriginal()
    {
        await using var db = Db();
        var notificacion = Fallida(Norte);
        db.NotificacionesDestinatario.Add(notificacion);
        await db.SaveChangesAsync();
        var publicador = new PublicadorFalso();

        await Servicio(db, publicador).ReintentarNotificacionAsync(notificacion.Id, Norte);

        Assert.Equal(EstadoNotificacionDestinatario.Pendiente, notificacion.Estado);
        Assert.Equal("{}", Assert.Single(publicador.Publicadas));
    }

    [Fact]
    public async Task SiLaColaNoResponde_SigueEnFallidasParaReintentar()
    {
        await using var db = Db();
        var notificacion = Fallida(Norte);
        db.NotificacionesDestinatario.Add(notificacion);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(db, new PublicadorFalso { Falla = true }).ReintentarNotificacionAsync(notificacion.Id, Norte));

        Assert.Equal(EstadoNotificacionDestinatario.Fallida, notificacion.Estado);
    }

    private static NotificacionDestinatario Crear(Guid operador) =>
        NotificacionDestinatario.Crear(
            new EventoEstadoEnvio(Guid.NewGuid(), DateTimeOffset.UtcNow, operador, Guid.NewGuid(), "EnDeposito", "EnTransito"), "{}", Ahora);

    private static NotificacionDestinatario Fallida(Guid operador)
    {
        var notificacion = Crear(operador);
        while (notificacion.RegistrarFallo("error", 0, Ahora)) { }
        return notificacion;
    }

    private static AvisosDbContext Db() =>
        new(new DbContextOptionsBuilder<AvisosDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ServicioAvisos Servicio(AvisosDbContext db, IPublicadorAvisos publicador) =>
        new(db, publicador, Options.Create(new OpcionesAvisos()));

    private sealed class PublicadorFalso : IPublicadorAvisos
    {
        public bool Falla { get; init; }
        public List<string> Publicadas { get; } = [];

        public Task PublicarEntregaAsync(Guid entregaId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PublicarResultadoAsync(ResultadoAviso resultado, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PublicarNotificacionAsync(Guid eventId, string eventoJson, CancellationToken cancellationToken = default)
        {
            if (Falla)
                throw new InvalidOperationException("Cola caída.");
            Publicadas.Add(eventoJson);
            return Task.CompletedTask;
        }
    }
}
