using Plataforma.Domain;

namespace Plataforma.Domain.Tests;

public class RedespachoParcialTests
{
    [Fact]
    public void EntregadoParcialmente_LiberaRutaYPuedeVolverAAsignarse()
    {
        var envio = Envio.Admitir(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Dest",
            "Calle 1");
        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        var rutaId = Guid.NewGuid();
        envio.AsignarARuta(rutaId);
        envio.CambiarEstado(EstadoEnvio.EnTransito);

        envio.CambiarEstado(EstadoEnvio.EntregadoParcialmente);

        Assert.Null(envio.RutaId);
        Assert.True(TransicionesEnvio.EsValida(envio.Estado, EstadoEnvio.AsignadoARuta));

        var nuevaRuta = Guid.NewGuid();
        envio.AsignarARuta(nuevaRuta);
        Assert.Equal(EstadoEnvio.AsignadoARuta, envio.Estado);
        Assert.Equal(nuevaRuta, envio.RutaId);
    }

    [Fact]
    public void Capacidad_SoloCuentaBultosDisponiblesParaNuevaRuta()
    {
        var entregado = Bulto.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "B1", pesoKg: 10m, altoCm: 10, anchoCm: 10, largoCm: 10);
        entregado.CambiarEstado(EstadoBulto.EnRuta);
        entregado.CambiarEstado(EstadoBulto.Entregado);

        var pendiente = Bulto.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "B2", pesoKg: 2m, altoCm: 2, anchoCm: 2, largoCm: 2);
        pendiente.CambiarEstado(EstadoBulto.EnRuta);
        pendiente.CambiarEstado(EstadoBulto.NoEntregado, "ausente");
        pendiente.CambiarEstado(EstadoBulto.PendienteNuevaVisita, "ausente");

        var despachables = new[] { entregado, pendiente }.Where(b => b.DisponibleParaNuevaRuta).ToList();
        Assert.Single(despachables);
        Assert.Equal(2m, despachables.Sum(b => b.PesoKg));
    }
}
