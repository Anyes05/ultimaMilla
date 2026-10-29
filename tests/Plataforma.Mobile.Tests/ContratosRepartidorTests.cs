using System.Text.Json.Nodes;
using Plataforma.Mobile.Data;
using Plataforma.Mobile.Models;
using Plataforma.Mobile.Services;

namespace Plataforma.Mobile.Tests;

public sealed class ContratosRepartidorTests
{
    [Fact]
    public void LoginLeeLosEjemplosYRechazaCredencialesInvalidas()
    {
        var solicitud = ContratosRepartidor.LeerEjemplo("login.request.json");
        var respuesta = ServidorRepartidorDemo.Login(solicitud);
        Assert.NotNull(respuesta);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(respuesta),
            JsonNode.Parse(ContratosRepartidor.LeerEjemplo("login.response.json"))));
        var sesion = ContratosRepartidor.Leer<LoginRespuesta>(respuesta);
        Assert.Equal("repartidor", sesion.Rol);
        Assert.False(string.IsNullOrWhiteSpace(sesion.Token));
        Assert.Null(ServidorRepartidorDemo.Login(
            ContratosRepartidor.Escribir(new LoginSolicitud("alvaro@demo.test", "incorrecta"))));
    }

    [Fact]
    public void RutaSeLeeComoListaConCamposDelBackend()
    {
        var rutas = ContratosRepartidor.Leer<RutaContrato[]>(ServidorRepartidorDemo.DescargarRutas());
        var ruta = Assert.Single(rutas);
        Assert.Equal(new DateOnly(2026, 10, 15), ruta.Fecha);
        Assert.Equal(3, ruta.Operador.MaximoIntentos);
        Assert.Equal(3, ruta.Envios.Count);
        Assert.All(ruta.Envios, envio =>
        {
            Assert.NotEqual(Guid.Empty, envio.Id);
            Assert.NotEmpty(envio.DireccionEntrega);
            Assert.NotEmpty(envio.EmailDestinatario);
            Assert.Equal(EstadoEnvio.EnTransito, envio.Estado);
            Assert.Equal(0, envio.IntentosRealizados);
        });
    }

    [Fact]
    public void DobleCalculaLaRespuestaDeEjemploDesdeElLoteJson()
    {
        var rutaContrato = Assert.Single(
            ContratosRepartidor.Leer<RutaContrato[]>(ServidorRepartidorDemo.DescargarRutas()));
        var ruta = CrearRuta(rutaContrato);
        var estados = CrearEstados(ruta);
        var resultado = ServidorRepartidorDemo.Sincronizar(
            ContratosRepartidor.LeerEjemplo("sincronizacion.request.json"), ruta, estados);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(resultado),
            JsonNode.Parse(ContratosRepartidor.LeerEjemplo("sincronizacion.response.json"))));
        Assert.Equal(EstadoEnvio.Entregado, estados[ruta.Envios[0].Id]);
        Assert.Equal(EstadoEnvio.Extraviado, estados[ruta.Envios[2].Id]);
    }

    [Fact]
    public void RespuestaNoEsFijaYElJsonConservaIdYHoraDelComprobante()
    {
        var contrato = Assert.Single(
            ContratosRepartidor.Leer<RutaContrato[]>(ServidorRepartidorDemo.DescargarRutas()));
        var ruta = CrearRuta(contrato);
        var estados = CrearEstados(ruta);
        var ejemplo = ContratosRepartidor.Leer<SincronizacionSolicitud>(
            ContratosRepartidor.LeerEjemplo("sincronizacion.request.json"));
        var evento = ejemplo.Eventos[0] with { NombreReceptor = "Otro receptor", Observaciones = "Otra entrega" };
        var lote = new SincronizacionSolicitud(ruta.Fecha, [evento]);
        var json = ContratosRepartidor.Escribir(lote);
        var releido = ContratosRepartidor.Leer<SincronizacionSolicitud>(json);
        Assert.Equal(evento, Assert.Single(releido.Eventos));
        Assert.Contains("\"estado\":\"Entregado\"", json);
        Assert.Contains("\"eventoId\"", json);
        var respuesta = ContratosRepartidor.Leer<SincronizacionRespuesta>(
            ServidorRepartidorDemo.Sincronizar(json, ruta, estados));
        Assert.Single(respuesta.Aplicados);
        Assert.Empty(respuesta.Rechazados);
        var vacia = ContratosRepartidor.Leer<SincronizacionRespuesta>(
            ServidorRepartidorDemo.Sincronizar(
                ContratosRepartidor.Escribir(new SincronizacionSolicitud(ruta.Fecha, [])), ruta, estados));
        Assert.Empty(vacia.Aplicados);
        Assert.Empty(vacia.Rechazados);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("esto no es JSON")]
    [InlineData("{\"fecha\":\"2026-10-15\",\"eventos\":[{\"estado\":2}]}")]
    public void JsonInvalidoInformaError(string json)
    {
        Assert.Throws<InvalidOperationException>(() => ContratosRepartidor.Leer<SincronizacionSolicitud>(json));
    }

    [Fact]
    public void NoAceptaEventosDuplicadosNiFechasAjenasALaRuta()
    {
        var ruta = CrearRuta(Assert.Single(
            ContratosRepartidor.Leer<RutaContrato[]>(ServidorRepartidorDemo.DescargarRutas())));
        var estados = CrearEstados(ruta);
        var original = ContratosRepartidor.Leer<SincronizacionSolicitud>(
            ContratosRepartidor.LeerEjemplo("sincronizacion.request.json"));
        var duplicado = original with { Eventos = [original.Eventos[0], original.Eventos[0]] };
        Assert.Throws<InvalidOperationException>(() =>
            ServidorRepartidorDemo.Sincronizar(ContratosRepartidor.Escribir(duplicado), ruta, estados));
        Assert.Throws<InvalidOperationException>(() =>
            ServidorRepartidorDemo.Sincronizar(
                ContratosRepartidor.Escribir(original with { Fecha = ruta.Fecha.AddDays(1) }), ruta, estados));
        Assert.Equal(EstadoEnvio.EnTransito, estados[ruta.Envios[0].Id]);
    }

    [Fact]
    public void ClienteUsaRutaDelContratoYSincronizaColaSqliteConDatosRealesDelFormulario()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"Plataforma-contratos-{Guid.NewGuid():N}.db");
        try
        {
            var data = new RepartidorLocalData(path);
            var service = new RepartidorDemoService(data);
            var login = ContratosRepartidor.Leer<LoginSolicitud>(
                ContratosRepartidor.LeerEjemplo("login.request.json"));
            Assert.True(service.IniciarSesion(login.Usuario, login.Clave));
            var contrato = Assert.Single(
                ContratosRepartidor.Leer<RutaContrato[]>(ServidorRepartidorDemo.DescargarRutas()));
            var ruta = service.ObtenerRuta();
            Assert.Equal(contrato.Id, ruta.Id);
            Assert.Equal(contrato.Envios.Select(envio => envio.Id), ruta.Envios.Select(envio => envio.Id));
            foreach (var envio in ruta.Envios)
                service.RegistrarEntrega(envio.Id, "Nombre del formulario", "Observacion no predefinida");
            var comprobantes = service.ObtenerRuta().Envios.Select(envio => envio.Entrega).ToArray();
            Assert.Equal(new ResultadoSincronizacion(1, 2, 2), service.Sincronizar());
            var reiniciado = new RepartidorDemoService(new RepartidorLocalData(path));
            Assert.Equal(comprobantes, reiniciado.ObtenerRuta().Envios.Select(envio => envio.Entrega).ToArray());
            Assert.Equal(new ResultadoSincronizacion(0, 2, 2), reiniciado.Sincronizar());

            // Una base previa usa otros UUID; ingresar no debe reemplazarla por el ejemplo.
            var previa = data.Leer();
            var idAnterior = Guid.NewGuid();
            data.Guardar(previa with { Ruta = previa.Ruta! with { Id = idAnterior } });
            Assert.True(reiniciado.IniciarSesion(login.Usuario, login.Clave));
            Assert.Equal(idAnterior, reiniciado.ObtenerRuta().Id);
            Assert.Equal(comprobantes, reiniciado.ObtenerRuta().Envios.Select(envio => envio.Entrega).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Ruta CrearRuta(RutaContrato contrato) =>
        new(contrato.Id, contrato.Fecha, contrato.Operador.Nombre,
            contrato.Envios.Select(envio => new Envio(envio.Id, envio.NumeroSeguimiento, envio.Destinatario,
                envio.DireccionEntrega, envio.Estado, null, EstadoSincronizacion.SinCambios, null)).ToArray());

    private static Dictionary<Guid, EstadoEnvio> CrearEstados(Ruta ruta) =>
        new()
        {
            [ruta.Envios[0].Id] = EstadoEnvio.EnTransito,
            [ruta.Envios[1].Id] = EstadoEnvio.Entregado,
            [ruta.Envios[2].Id] = EstadoEnvio.Extraviado
        };
}
