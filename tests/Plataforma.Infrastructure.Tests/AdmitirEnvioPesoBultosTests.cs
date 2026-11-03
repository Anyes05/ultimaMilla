using Plataforma.Application;

namespace Plataforma.Infrastructure.Tests;

public class AdmitirEnvioPesoBultosTests
{
    [Fact]
    public void RepartirPeso_UnBulto_LlevaTodoElPeso()
    {
        var pesos = AdmitirEnvio.RepartirPesoEntreBultos(2.5m, 1);
        Assert.Equal(new[] { 2.5m }, pesos);
    }

    [Fact]
    public void RepartirPeso_VariosBultos_SumaElTotalYNingunoEsCero()
    {
        var pesos = AdmitirEnvio.RepartirPesoEntreBultos(3m, 3);
        Assert.Equal(3, pesos.Length);
        Assert.Equal(3m, pesos.Sum());
        Assert.All(pesos, p => Assert.True(p > 0));
    }

    [Fact]
    public void RepartirPeso_Cero_Falla()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AdmitirEnvio.RepartirPesoEntreBultos(0, 2));
    }
}
