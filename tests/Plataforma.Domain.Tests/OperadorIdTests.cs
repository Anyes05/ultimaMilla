using Plataforma.Domain;

namespace Plataforma.Domain.Tests;

public class OperadorIdTests
{
    private readonly Guid _operadorId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void LasEntidadesDelModeloLlevanElOperador()
    {
        var comercio = Comercio.Registrar(_id(), _operadorId, "Comercio Norte", "Norte SA", "123", "Av. Italia 1");
        var usuario = UsuarioPlataforma.Registrar(_id(), _operadorId, "Ana", "ana@norte.test", "ana", "hash", RolUsuario.Despachador, null);
        var repartidor = Repartidor.Registrar(_id(), _operadorId, "Luis", "400", "099");
        var tarifa = ReglaTarifaria.Crear(_id(), _operadorId, 100, 10, 5, 20, 30);
        var vigencia = VigenciaTarifa.Crear(_id(), _operadorId, tarifa.Id, DateTime.UtcNow, DateTime.UtcNow.AddDays(1));
        var zona = ZonaCobertura.Definir(_id(), _operadorId, "Centro", "[]");
        var regla = ReglaOperativa.Crear(_id(), _operadorId, 3, 24, true, true, false, 12, DateTime.UtcNow, DateTime.UtcNow.AddDays(1));
        var envioId = _id();
        var destinatario = Destinatario.Registrar(_id(), _operadorId, envioId, "Juan Pérez", "1", "099");
        var direccion = Direccion.Registrar(_id(), _operadorId, envioId, "Av. Italia", "1234");
        var historial = HistorialEstado.Registrar(_id(), _operadorId, envioId, EstadoEnvio.Admitido, DateTime.UtcNow);
        var intento = IntentoEntrega.Registrar(_id(), _operadorId, envioId, 1, DateTime.UtcNow, "NoEntregado");
        var novedad = Novedad.Registrar(_id(), _operadorId, envioId, "Ausente", DateTime.UtcNow);
        var incidencia = Incidencia.Registrar(_id(), _operadorId, envioId, "Dirección incorrecta", DateTime.UtcNow);
        var liquidacion = Liquidacion.Abrir(_id(), _operadorId, comercio.Id, DateTime.UtcNow.Date, DateTime.UtcNow.Date, 0);
        var webhook = SuscripcionWebhook.Registrar(_id(), _operadorId, comercio.Id, "https://comercio.test/hook");
        var aviso = AvisoComercio.Crear(_id(), _operadorId, comercio.Id, "Nuevo envío", DateTime.UtcNow);
        var ruta = Ruta.Programar(_operadorId, repartidor.Id, DateOnly.FromDateTime(DateTime.UtcNow));
        var parada = Parada.Programar(_id(), _operadorId, ruta.Id, envioId, 1);
        var vehiculo = Vehiculo.Registrar(_id(), _operadorId, "ABC1234", "Moto");
        var prueba = PruebaDeEntrega.Registrar(_id(), _operadorId, envioId, "", "", DateTime.UtcNow);
        var envio = Envio.Admitir(_operadorId, comercio.Id, "Juan Pérez", "Av. Italia 1234");

        Assert.Equal(_operadorId, comercio.OperadorId);
        Assert.Equal(_operadorId, usuario.OperadorId);
        Assert.Equal(_operadorId, repartidor.OperadorId);
        Assert.Equal(_operadorId, tarifa.OperadorId);
        Assert.Equal(_operadorId, vigencia.OperadorId);
        Assert.Equal(_operadorId, zona.OperadorId);
        Assert.Equal(_operadorId, regla.OperadorId);
        Assert.Equal(_operadorId, destinatario.OperadorId);
        Assert.Equal(_operadorId, direccion.OperadorId);
        Assert.Equal(_operadorId, historial.OperadorId);
        Assert.Equal(_operadorId, intento.OperadorId);
        Assert.Equal(_operadorId, novedad.OperadorId);
        Assert.Equal(_operadorId, incidencia.OperadorId);
        Assert.Equal(_operadorId, liquidacion.OperadorId);
        Assert.Equal(_operadorId, webhook.OperadorId);
        Assert.Equal(_operadorId, aviso.OperadorId);
        Assert.Equal(_operadorId, ruta.OperadorId);
        Assert.Equal(_operadorId, parada.OperadorId);
        Assert.Equal(_operadorId, vehiculo.OperadorId);
        Assert.Equal(_operadorId, prueba.OperadorId);
        Assert.Equal(_operadorId, envio.OperadorId);
    }

    [Fact]
    public void RechazaEntidadSinOperador()
    {
        Assert.Throws<ArgumentException>(() =>
            Comercio.Registrar(Guid.NewGuid(), Guid.Empty, "Norte", "", "", ""));
    }

    private static Guid _id() => Guid.NewGuid();
}
