using Plataforma.Avisos;

namespace Plataforma.Avisos.Tests;

public class FirmaAvisoTests
{
    private const string Secreto = "secreto-de-prueba-123456";
    private const string Cuerpo = """{"eventId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","estadoNuevo":"Entregado"}""";
    private static readonly DateTimeOffset Ahora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirmaValida_SeVerifica()
    {
        var timestamp = Ahora.ToUnixTimeSeconds();
        var firma = FirmaAviso.Firmar(Secreto, timestamp, Cuerpo);

        Assert.StartsWith(FirmaAviso.Prefijo, firma);
        Assert.True(FirmaAviso.Verificar(Secreto, timestamp.ToString(), Cuerpo, firma, Ahora));
    }

    [Fact]
    public void CuerpoAlterado_SeRechaza()
    {
        var timestamp = Ahora.ToUnixTimeSeconds();
        var firma = FirmaAviso.Firmar(Secreto, timestamp, Cuerpo);

        Assert.False(FirmaAviso.Verificar(Secreto, timestamp.ToString(), Cuerpo.Replace("Entregado", "Extraviado"), firma, Ahora));
    }

    [Fact]
    public void OtroSecreto_SeRechaza()
    {
        var timestamp = Ahora.ToUnixTimeSeconds();
        var firma = FirmaAviso.Firmar("otro-secreto-distinto-999", timestamp, Cuerpo);

        Assert.False(FirmaAviso.Verificar(Secreto, timestamp.ToString(), Cuerpo, firma, Ahora));
    }

    [Fact]
    public void AvisoViejo_SeRechazaPorRepeticion()
    {
        var timestamp = Ahora.AddMinutes(-10).ToUnixTimeSeconds();
        var firma = FirmaAviso.Firmar(Secreto, timestamp, Cuerpo);

        Assert.False(FirmaAviso.Verificar(Secreto, timestamp.ToString(), Cuerpo, firma, Ahora));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-un-numero")]
    public void SinTimestamp_SeRechaza(string? timestamp)
    {
        var firma = FirmaAviso.Firmar(Secreto, Ahora.ToUnixTimeSeconds(), Cuerpo);

        Assert.False(FirmaAviso.Verificar(Secreto, timestamp, Cuerpo, firma, Ahora));
    }
}
