using Plataforma.Domain;

namespace Plataforma.Domain.Tests;

public class TransicionEnvioPublicacionTests
{
    [Fact]
    public void DestinosDesde_Admitido_IncluyeEnDeposito()
    {
        var destinos = TransicionesEnvio.DestinosDesde(EstadoEnvio.Admitido);

        Assert.Contains(EstadoEnvio.EnDeposito, destinos);
        Assert.Contains(EstadoEnvio.Extraviado, destinos);
    }

    [Fact]
    public void DestinosDesde_EstadoTerminal_QuedaVacio()
    {
        Assert.Empty(TransicionesEnvio.DestinosDesde(EstadoEnvio.Entregado));
    }
}
