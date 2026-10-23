using System;
using Plataforma.Domain;
using Xunit;

namespace Plataforma.Domain.Tests;

public class EnvioTests
{
    private readonly Guid _operadorId = Guid.NewGuid();
    private readonly Guid _comercioId = Guid.NewGuid();

    [Fact]
    public void AdmitirEnvio_DeberiaCrearEnvioEnEstadoAdmitido()
    {
        // Act
        var envio = Envio.Admitir(_operadorId, _comercioId, "Juan Pérez", "Av. Italia 1234");

        // Assert
        Assert.NotNull(envio);
        Assert.Equal(EstadoEnvio.Admitido, envio.Estado);
        Assert.Equal("Juan Pérez", envio.Destinatario);
        Assert.Equal("Av. Italia 1234", envio.DireccionEntrega);
    }

    [Fact]
    public void CambiarEstado_TransicionValida_DeberiaActualizarEstado()
    {
        // Arrange
        var envio = Envio.Admitir(_operadorId, _comercioId, "Maria Lopez", "18 de Julio 5678");

        // Act
        envio.CambiarEstado(EstadoEnvio.EnDeposito);

        // Assert
        Assert.Equal(EstadoEnvio.EnDeposito, envio.Estado);
    }

    [Fact]
    public void CambiarEstado_TransicionInvalida_DeberiaLanzarExcepcion()
    {
        // Arrange
        var envio = Envio.Admitir(_operadorId, _comercioId, "Carlos Ruiz", "Colonia 901");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => envio.CambiarEstado(EstadoEnvio.Entregado));
    }

    [Fact]
    public void CambiarEstado_VuelveADeposito_LimpiaRutaId()
    {
        var envio = Envio.Admitir(_operadorId, _comercioId, "Ana", "Calle 1");
        envio.CambiarEstado(EstadoEnvio.EnDeposito);
        var rutaId = Guid.NewGuid();
        envio.AsignarARuta(rutaId);

        Assert.Equal(rutaId, envio.RutaId);

        envio.CambiarEstado(EstadoEnvio.EnDeposito);

        Assert.Equal(EstadoEnvio.EnDeposito, envio.Estado);
        Assert.Null(envio.RutaId);
    }

    [Fact]
    public void Admitir_NumeroSeguimientoEsOpacoYUnico()
    {
        var a = Envio.Admitir(_operadorId, _comercioId, "A", "Calle A");
        var b = Envio.Admitir(_operadorId, _comercioId, "B", "Calle B");

        Assert.StartsWith("ENV-", a.NumeroSeguimiento);
        Assert.True(a.NumeroSeguimiento.Length > 10);
        Assert.NotEqual(a.NumeroSeguimiento, b.NumeroSeguimiento);
    }
}