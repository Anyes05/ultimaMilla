using Plataforma.Domain;
namespace Plataforma.Domain.Tests;

public class TransicionesEnvioTests
{
    public static IEnumerable<object[]> TransicionesValidas =>
        new List<object[]>
        {
            new object[] { EstadoEnvio.Admitido, EstadoEnvio.EnDeposito },
            new object[] { EstadoEnvio.Admitido, EstadoEnvio.Extraviado },

            new object[] { EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta },
            new object[] { EstadoEnvio.EnDeposito, EstadoEnvio.Extraviado },

            new object[] { EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito },
            new object[] { EstadoEnvio.AsignadoARuta, EstadoEnvio.EnDeposito },

            new object[] { EstadoEnvio.EnTransito, EstadoEnvio.Entregado },
            new object[] { EstadoEnvio.EnTransito, EstadoEnvio.NoEntregado },
            new object[] { EstadoEnvio.EnTransito, EstadoEnvio.Extraviado },

            new object[] { EstadoEnvio.NoEntregado, EstadoEnvio.Reprogramado },
            new object[] { EstadoEnvio.NoEntregado, EstadoEnvio.EnDevolucion },
            new object[] { EstadoEnvio.NoEntregado, EstadoEnvio.Extraviado },

            new object[] { EstadoEnvio.Reprogramado, EstadoEnvio.AsignadoARuta },

            new object[] { EstadoEnvio.EnDevolucion, EstadoEnvio.Devuelto }
        };

    [Theory]
    [MemberData(nameof(TransicionesValidas))]
    public void Transicion_valida_es_aceptada(
        EstadoEnvio origen,
        EstadoEnvio destino)
    {
        Assert.True(
            TransicionesEnvio.EsValida(origen, destino));
    }

    public static IEnumerable<object[]> TransicionesInvalidas()
    {
        var todos = Enum.GetValues<EstadoEnvio>();

        foreach (var origen in todos)
        {
            foreach (var destino in todos)
            {
                bool esValida = TransicionesValidas.Any(t =>
                    (EstadoEnvio)t[0] == origen &&
                    (EstadoEnvio)t[1] == destino);

                if (!esValida)
                {
                    yield return new object[] { origen, destino };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(TransicionesInvalidas))]
    public void Transicion_invalida_es_rechazada(
        EstadoEnvio origen,
        EstadoEnvio destino)
    {
        Assert.False(
            TransicionesEnvio.EsValida(origen, destino));
    }
}
