using Plataforma.Domain;

namespace Plataforma.Domain.Tests;

public class BultoYDerivacionTests
{
    [Fact]
    public void Transicion_bulto_valida_e_invalida()
    {
        Assert.True(TransicionesBulto.EsValida(EstadoBulto.Pendiente, EstadoBulto.EnRuta));
        Assert.True(TransicionesBulto.EsValida(EstadoBulto.EnRuta, EstadoBulto.Entregado));
        Assert.False(TransicionesBulto.EsValida(EstadoBulto.Entregado, EstadoBulto.EnRuta));
    }

    [Fact]
    public void Derivacion_todos_entregados_es_entregado()
    {
        var estado = DerivacionEstadoEnvio.DesdeBultos(
        [
            EstadoBulto.Entregado,
            EstadoBulto.Entregado
        ]);
        Assert.Equal(EstadoEnvio.Entregado, estado);
    }

    [Fact]
    public void Derivacion_mezcla_es_parcial()
    {
        var estado = DerivacionEstadoEnvio.DesdeBultos(
        [
            EstadoBulto.Entregado,
            EstadoBulto.PendienteNuevaVisita
        ]);
        Assert.Equal(EstadoEnvio.EntregadoParcialmente, estado);
    }

    [Fact]
    public void Envio_aplica_estado_derivado_desde_en_transito()
    {
        var envio = Envio.Admitir(Guid.NewGuid(), Guid.NewGuid(), "Dest", "Calle");
        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        envio.AsignarARuta(Guid.NewGuid());
        envio.CambiarEstado(EstadoEnvio.EnTransito);

        envio.AplicarEstadoDerivadoDeBultos(
        [
            EstadoBulto.Entregado,
            EstadoBulto.NoEntregado
        ]);

        Assert.Equal(EstadoEnvio.EntregadoParcialmente, envio.Estado);
        Assert.True(envio.Version > 0);
    }

    [Fact]
    public void Bulto_exige_motivo_si_no_entregado()
    {
        var bulto = Bulto.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "COD-1", pesoKg: 1);
        bulto.CambiarEstado(EstadoBulto.EnRuta);
        Assert.Throws<InvalidOperationException>(() => bulto.CambiarEstado(EstadoBulto.NoEntregado));
        bulto.CambiarEstado(EstadoBulto.NoEntregado, "Ausente");
        Assert.Equal(EstadoBulto.NoEntregado, bulto.Estado);
    }

    [Fact]
    public void Pendiente_nueva_visita_disponible_para_nueva_ruta()
    {
        Assert.True(DerivacionEstadoEnvio.DisponibleParaNuevaRuta(EstadoBulto.PendienteNuevaVisita));
        Assert.False(DerivacionEstadoEnvio.DisponibleParaNuevaRuta(EstadoBulto.Entregado));
    }
}
