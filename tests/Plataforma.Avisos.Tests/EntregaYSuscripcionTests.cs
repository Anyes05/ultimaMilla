using Plataforma.Avisos;

namespace Plataforma.Avisos.Tests;

public class EntregaYSuscripcionTests
{
    private static readonly DateTime Ahora = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static SuscripcionAviso Suscripcion(params string[] tipos) =>
        SuscripcionAviso.Crear(Guid.NewGuid(), Guid.NewGuid(), "https://comercio.test/avisos", "secreto-de-prueba-123456", tipos, true, Ahora);

    private static EntregaAviso Entrega() =>
        EntregaAviso.Crear(Guid.NewGuid(), Suscripcion(), Guid.NewGuid(), "Entregado", "{}", Ahora);

    [Fact]
    public void SuscripcionSinTipos_AceptaTodos()
    {
        var suscripcion = Suscripcion();

        Assert.True(suscripcion.Acepta("Entregado"));
        Assert.True(suscripcion.Acepta("EnTransito"));
    }

    [Fact]
    public void SuscripcionConTipos_FiltraPorEvento()
    {
        var suscripcion = Suscripcion("Entregado", "NoEntregado");

        Assert.True(suscripcion.Acepta("Entregado"));
        Assert.False(suscripcion.Acepta("EnTransito"));
    }

    [Fact]
    public void SuscripcionInactiva_NoAcepta()
    {
        var suscripcion = Suscripcion();
        suscripcion.Actualizar(suscripcion.Url, suscripcion.Secreto, [], activa: false, Ahora);

        Assert.False(suscripcion.Acepta("Entregado"));
    }

    [Fact]
    public void TiposDesconocidos_SeDescartan()
    {
        var suscripcion = Suscripcion("Entregado", "NoExiste");

        Assert.Equal(["Entregado"], suscripcion.Tipos);
    }

    [Theory]
    [InlineData("ftp://comercio.test")]
    [InlineData("no-es-url")]
    public void UrlInvalida_SeRechaza(string url) =>
        Assert.Throws<ArgumentException>(() =>
            SuscripcionAviso.Crear(Guid.NewGuid(), Guid.NewGuid(), url, "secreto-de-prueba-123456", [], true, Ahora));

    [Fact]
    public void SecretoCorto_SeRechaza() =>
        Assert.Throws<ArgumentException>(() =>
            SuscripcionAviso.Crear(Guid.NewGuid(), Guid.NewGuid(), "https://comercio.test", "corto", [], true, Ahora));

    [Fact]
    public void Fallos_ReintentaHastaAgotarYQuedaFallida()
    {
        var entrega = Entrega();
        const int maximoReintentos = 3;

        // Envío original + 3 reintentos = 4 intentos; recién el cuarto fallo agota.
        Assert.True(entrega.RegistrarFallo("503", 503, maximoReintentos, Ahora));
        Assert.True(entrega.RegistrarFallo("503", 503, maximoReintentos, Ahora));
        Assert.True(entrega.RegistrarFallo("503", 503, maximoReintentos, Ahora));
        Assert.False(entrega.RegistrarFallo("503", 503, maximoReintentos, Ahora));

        Assert.Equal(EstadoEntrega.Fallido, entrega.Estado);
        Assert.Equal(4, entrega.Intentos);
    }

    [Fact]
    public void EntregaEnCurso_NoPuedeTomarseDosVeces()
    {
        var entrega = Entrega();
        entrega.Tomar(Ahora);

        Assert.False(entrega.PuedeTomarse(Ahora.AddSeconds(10)));
        Assert.Throws<InvalidOperationException>(() => entrega.Tomar(Ahora.AddSeconds(10)));
    }

    [Fact]
    public void TomaAbandonada_SePuedeRetomar()
    {
        var entrega = Entrega();
        entrega.Tomar(Ahora);

        Assert.True(entrega.PuedeTomarse(Ahora + EntregaAviso.TomaVencida + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void EntregaEntregada_NoSeVuelveATomar()
    {
        var entrega = Entrega();
        entrega.Tomar(Ahora);
        entrega.MarcarEntregada(200, Ahora);

        Assert.False(entrega.PuedeTomarse(Ahora.AddHours(1)));
    }

    [Fact]
    public void ReintentoManual_SoloDesdeFallido()
    {
        var entrega = Entrega();
        Assert.Throws<InvalidOperationException>(() => entrega.ReiniciarParaReintento(Ahora));

        entrega.RegistrarFallo("caído", 503, maximoReintentos: 0, Ahora);
        var eventId = entrega.EventId;
        entrega.ReiniciarParaReintento(Ahora);

        Assert.Equal(EstadoEntrega.Pendiente, entrega.Estado);
        Assert.Equal(0, entrega.Intentos);
        Assert.Equal(eventId, entrega.EventId);
    }

    [Fact]
    public void UrlHttp_SoloParaHostsPermitidos()
    {
        var opciones = new OpcionesAvisos { HostsHttpPermitidos = ["receptor-prueba"] };

        Assert.True(opciones.UrlPermitida("https://cualquiera.test/avisos"));
        Assert.True(opciones.UrlPermitida("http://receptor-prueba:8080/avisos"));
        Assert.False(opciones.UrlPermitida("http://tallernet-redis:6379"));
    }

    [Fact]
    public void Esperas_SonCrecientesPorDefecto()
    {
        var esperas = new OpcionesAvisos().Esperas;

        Assert.Equal(esperas.OrderBy(segundos => segundos), esperas);
        Assert.Equal(3, new OpcionesAvisos().MaximoReintentos);
    }
}
