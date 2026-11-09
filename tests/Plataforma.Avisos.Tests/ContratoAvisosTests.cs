using System.Text.Json;
using Plataforma.Avisos;

namespace Plataforma.Avisos.Tests;

/// <summary>
/// RF 36: "el cambio no puede afectar" a los comercios que ya reciben avisos, así que el contrato se versiona.
/// v1 queda congelado; v2 suma número de seguimiento y los estados nuevos.
/// </summary>
public class ContratoAvisosTests
{
    private static readonly DateTime Ahora = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static readonly EventoEstadoEnvio Evento = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero),
        Guid.NewGuid(),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "EnTransito",
        "Entregado");

    private static SuscripcionAviso Suscripcion(int version, params string[] tipos) =>
        SuscripcionAviso.Crear(Guid.NewGuid(), Guid.NewGuid(), "https://comercio.test/avisos", "secreto-de-prueba-123456", tipos, true, Ahora, version);

    [Fact]
    public void CuerpoV1_NoCambia()
    {
        // Es el JSON exacto que ya reciben los comercios: si este test falla, se rompió el contrato v1.
        var cuerpo = ContratoAvisos.Armar(ContratoAvisos.V1, Evento, "ENV-123");

        Assert.Equal(
            """{"eventId":"11111111-1111-1111-1111-111111111111","tipo":"EnvioEstadoCambiado","fecha":"2026-11-03T12:00:00+00:00","envioId":"22222222-2222-2222-2222-222222222222","estadoAnterior":"EnTransito","estadoNuevo":"Entregado"}""",
            cuerpo);
    }

    [Fact]
    public void CuerpoV2_TraeVersionYNumeroDeSeguimiento()
    {
        using var json = JsonDocument.Parse(ContratoAvisos.Armar(ContratoAvisos.V2, Evento, "ENV-123"));
        var raiz = json.RootElement;

        Assert.Equal(2, raiz.GetProperty("version").GetInt32());
        Assert.Equal("EnvioEstadoCambiado", raiz.GetProperty("tipo").GetString());
        var envio = raiz.GetProperty("envio");
        Assert.Equal("ENV-123", envio.GetProperty("numeroSeguimiento").GetString());
        Assert.Equal("Entregado", envio.GetProperty("estadoNuevo").GetString());
    }

    [Theory]
    [InlineData(ContratoAvisos.V1)]
    [InlineData(ContratoAvisos.V2)]
    public void Leer_EntiendeLasDosVersiones(int version)
    {
        var aviso = ContratoAvisos.Leer(ContratoAvisos.Armar(version, Evento, "ENV-123"));

        Assert.NotNull(aviso);
        Assert.Equal(Evento.EventId, aviso.EventId);
        Assert.Equal(Evento.EnvioId, aviso.EnvioId);
        Assert.Equal("EnTransito", aviso.EstadoAnterior);
        Assert.Equal("Entregado", aviso.EstadoNuevo);
    }

    [Fact]
    public void EntregaParcial_SoloLlegaAV2()
    {
        Assert.False(Suscripcion(ContratoAvisos.V1).Acepta("EntregadoParcialmente"));
        Assert.True(Suscripcion(ContratoAvisos.V2).Acepta("EntregadoParcialmente"));
        Assert.True(Suscripcion(ContratoAvisos.V2).Acepta("Entregado"));
    }

    [Fact]
    public void SuscripcionV1_DescartaTiposSoloV2()
    {
        var suscripcion = Suscripcion(ContratoAvisos.V1, "Entregado", "EntregadoParcialmente");

        Assert.Equal(["Entregado"], suscripcion.Tipos);
    }

    [Fact]
    public void PorDefecto_EsV1()
    {
        // Las suscripciones existentes (y las que no eligen) siguen recibiendo el formato original.
        var suscripcion = SuscripcionAviso.Crear(Guid.NewGuid(), Guid.NewGuid(), "https://comercio.test/avisos", "secreto-de-prueba-123456", [], true, Ahora);

        Assert.Equal(ContratoAvisos.V1, suscripcion.VersionContrato);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void VersionInexistente_SeRechaza(int version) =>
        Assert.Throws<ArgumentException>(() => Suscripcion(version));

    [Fact]
    public void Entrega_ConservaLaVersionConQueSeArmo()
    {
        // Si el comercio cambia de versión, los reintentos de avisos viejos mandan el cuerpo que se firmó.
        var suscripcion = Suscripcion(ContratoAvisos.V2);
        var entrega = EntregaAviso.Crear(Evento.EventId, suscripcion, Evento.EnvioId, Evento.EstadoNuevo,
            ContratoAvisos.Armar(suscripcion.VersionContrato, Evento, "ENV-123"), Ahora);

        suscripcion.Actualizar(suscripcion.Url, suscripcion.Secreto, [], true, Ahora, ContratoAvisos.V1);

        Assert.Equal(ContratoAvisos.V2, entrega.VersionContrato);
        Assert.Equal(ContratoAvisos.V1, suscripcion.VersionContrato);
    }
}
