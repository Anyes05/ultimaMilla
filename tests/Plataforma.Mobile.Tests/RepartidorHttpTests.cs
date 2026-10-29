using System.Net;
using System.Text;
using Plataforma.Mobile.Data;
using Plataforma.Mobile.Models;
using Plataforma.Mobile.Services;

namespace Plataforma.Mobile.Tests;

public sealed partial class RepartidorHttpTests : IDisposable
{
    private readonly string directory = Path.Combine(AppContext.BaseDirectory, "RepartidorHttpTests", Guid.NewGuid().ToString("N"));
    private readonly Tokens tokens = new();
    private readonly Servidor handler = new();
    private readonly RepartidorLocalData data;
    private readonly HttpClient http;
    private readonly RepartidorHttpService service;

    public RepartidorHttpTests()
    {
        Directory.CreateDirectory(directory);
        data = new RepartidorLocalData(Path.Combine(directory, "api.db"));
        http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        service = new RepartidorHttpService(http, data, tokens);
    }

    [Fact]
    public async Task LoginUsaContratoBearerYDescargaSinGuardarTokenEnSQLite()
    {
        await Login();
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/api/repartidor/login", handler.Requests[0].Path);
        Assert.Equal("/api/repartidor/rutas/hoy", handler.Requests[1].Path);
        Assert.Null(handler.Requests[0].Authorization);
        Assert.Contains("\"usuario\":", handler.Requests[0].Body);
        Assert.Equal("Bearer demo.jwt.repartidor", handler.Requests[1].Authorization);
        Assert.Equal(3, service.ObtenerRuta().Envios.Count);
        Assert.Equal("demo.jwt.repartidor", tokens.Value);
        Assert.DoesNotContain("demo.jwt.repartidor", File.ReadAllText(Path.Combine(directory, "api.db")));
        Assert.NotNull(data.Leer().RepartidorId);
    }

    [Fact]
    public async Task EntregaOfflineSobreviveReinicioYSeSincronizaConElMismoEventoId()
    {
        await Login();
        var envio = service.ObtenerRuta().Envios[0];
        service.ConexionSimulada = false;
        service.RegistrarEntrega(envio.Id, "Receptor ficticio", "Puerta");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SincronizarAsync());
        var entrega = service.ObtenerRuta().Envios[0].Entrega;
        var reopened = new RepartidorHttpService(http,
            new RepartidorLocalData(Path.Combine(directory, "api.db")), tokens);
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta(
            [new AplicadoContrato(envio.Id, EstadoEnvio.Entregado, 0)], [])));
        Assert.Equal(new ResultadoSincronizacion(1, 0, 0), await reopened.SincronizarAsync());
        var request = ContratosRepartidor.Leer<SincronizacionSolicitud>(handler.Requests.Last().Body);
        Assert.Equal("/api/repartidor/sincronizacion", handler.Requests.Last().Path);
        Assert.Equal("Bearer demo.jwt.repartidor", handler.Requests.Last().Authorization);
        Assert.Equal(entrega!.Id, request.Eventos[0].EventoId);
        Assert.Equal(EstadoSincronizacion.Sincronizado, reopened.ObtenerRuta().Envios[0].Sincronizacion);
    }

    [Fact]
    public async Task RechazoConservaComprobanteYEstadoServidor()
    {
        await Login();
        var envio = service.ObtenerRuta().Envios[0];
        service.RegistrarEntrega(envio.Id, "Receptor", "");
        var comprobante = service.ObtenerRuta().Envios[0].Entrega;
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta([], [
            new RechazadoContrato(envio.Id, "Ya extraviado", EstadoEnvio.Extraviado)])));
        Assert.Equal(new ResultadoSincronizacion(0, 1, 1), await service.SincronizarAsync());
        Assert.Equal(comprobante, service.ObtenerRuta().Envios[0].Entrega);
        Assert.Equal(EstadoEnvio.Extraviado, service.ObtenerRuta().Envios[0].Estado);
        Assert.Throws<InvalidOperationException>(() => service.CerrarSesion());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task ErrorHttpNoBorraPendientes(int status)
    {
        await Login();
        service.RegistrarEntrega(service.ObtenerRuta().Envios[0].Id, "Receptor", "");
        handler.Respuestas.Enqueue(new HttpResponseMessage((HttpStatusCode)status));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SincronizarAsync());
        Assert.Equal(EstadoSincronizacion.Pendiente, service.ObtenerRuta().Envios[0].Sincronizacion);
    }

    [Fact]
    public async Task RedCaidaYTimeoutConservanLaColaSinUsarElDoble()
    {
        await Login();
        service.RegistrarEntrega(service.ObtenerRuta().Envios[0].Id, "Receptor", "");
        foreach (var error in new Exception[] { new HttpRequestException("Sin red"), new TaskCanceledException("Timeout") })
        {
            handler.Error = error;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SincronizarAsync());
            Assert.Equal(EstadoSincronizacion.Pendiente, service.ObtenerRuta().Envios[0].Sincronizacion);
        }
    }

    [Fact]
    public async Task RespuestaIncompletaODuplicadaNoConfirmaEntregas()
    {
        await Login();
        var id = service.ObtenerRuta().Envios[0].Id;
        service.RegistrarEntrega(id, "Receptor", "");
        foreach (var respuesta in new[]
        {
            new SincronizacionRespuesta([], []),
            new SincronizacionRespuesta([new AplicadoContrato(id, EstadoEnvio.Entregado, 0)],
                [new RechazadoContrato(id, "Duplicado", null)]),
            new SincronizacionRespuesta([new AplicadoContrato(Guid.NewGuid(), EstadoEnvio.Entregado, 0)], [])
        })
        {
            handler.Respuestas.Enqueue(Ok(respuesta));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SincronizarAsync());
            Assert.Equal(EstadoSincronizacion.Pendiente, service.ObtenerRuta().Envios[0].Sincronizacion);
        }
    }

    [Fact]
    public async Task LoginInvalidoNoCreaSesion()
    {
        handler.Respuestas.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        Assert.False(await service.IniciarSesionAsync("incorrecto", "incorrecto"));
        Assert.False(service.HaySesion);
        Assert.Null(tokens.Value);
    }

    [Fact]
    public async Task ReloginConservaColaYRechazaOtroOperador()
    {
        await Login();
        service.RegistrarEntrega(service.ObtenerRuta().Envios[0].Id, "Receptor", "");
        var anterior = service.ObtenerRuta().Envios[0].Entrega;
        handler.Respuestas.Enqueue(Ejemplo("login.response.json"));
        Assert.True(await service.IniciarSesionAsync("alvaro@demo.test", "Demo123!"));
        Assert.Equal(anterior, service.ObtenerRuta().Envios[0].Entrega);
        var sesion = ContratosRepartidor.Leer<LoginRespuesta>(ContratosRepartidor.LeerEjemplo("login.response.json"));
        handler.Respuestas.Enqueue(Ok(sesion with { OperadorId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IniciarSesionAsync("otro", "clave"));
        Assert.Equal(anterior, service.ObtenerRuta().Envios[0].Entrega);
    }

    [Fact]
    public async Task CeroYMultiplesRutasNoSeDescartanSilenciosamente()
    {
        handler.Respuestas.Enqueue(Ejemplo("login.response.json"));
        handler.Respuestas.Enqueue(Ok(Array.Empty<RutaContrato>()));
        Assert.True(await service.IniciarSesionAsync("alvaro@demo.test", "Demo123!"));
        Assert.Empty(service.ObtenerRuta().Envios);
        var ruta = ContratosRepartidor.Leer<RutaContrato[]>(ContratosRepartidor.LeerEjemplo("rutas-hoy.response.json"))[0];
        var segunda = ruta with { Id = Guid.NewGuid(), Envios = [ruta.Envios[0] with { Id = Guid.NewGuid() }] };
        handler.Respuestas.Enqueue(Ok(new[] { ruta, segunda }));
        await service.DescargarRutaAsync();
        Assert.Equal(4, service.ObtenerRuta().Envios.Count);
        service.RegistrarEntrega(service.ObtenerRuta().Envios[0].Id, "Receptor", "");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DescargarRutaAsync());
    }

    [Fact]
    public async Task ModoDemoYApiMantienenDatosSeparados()
    {
        await Login();
        var demo = new RepartidorDemoService(new RepartidorLocalData(Path.Combine(directory, "demo.db")));
        var seleccion = new RepartidorSeleccionService(demo, service);
        Assert.False(seleccion.ModoDemo);
        seleccion.ModoDemo = true;
        Assert.False(seleccion.HaySesion);
        Assert.True(await seleccion.IniciarSesionAsync("alvaro@demo.test", "Demo123!"));
        seleccion.RegistrarEntrega(seleccion.ObtenerRuta().Envios[0].Id, "Demo", "");
        seleccion.ModoDemo = false;
        Assert.All(seleccion.ObtenerRuta().Envios, envio => Assert.Null(envio.Entrega));
    }

    private async Task Login()
    {
        handler.Respuestas.Enqueue(Ejemplo("login.response.json"));
        handler.Respuestas.Enqueue(Ejemplo("rutas-hoy.response.json"));
        Assert.True(await service.IniciarSesionAsync("alvaro@demo.test", "Demo123!"));
    }

    private static HttpResponseMessage Ejemplo(string name) =>
        new(HttpStatusCode.OK) { Content = new StringContent(ContratosRepartidor.LeerEjemplo(name), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Ok<T>(T value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(ContratosRepartidor.Escribir(value), Encoding.UTF8, "application/json") };

    public void Dispose()
    {
        http.Dispose();
        Directory.Delete(directory, true);
    }

    private sealed class Tokens : ITokenRepartidor
    {
        public string? Value { get; private set; }
        public Task<string?> LeerAsync() => Task.FromResult(Value);
        public Task GuardarAsync(string token) { Value = token; return Task.CompletedTask; }
        public void Borrar() => Value = null;
    }

    private sealed class Servidor : HttpMessageHandler
    {
        public Queue<HttpResponseMessage> Respuestas { get; } = new();
        public List<(string Path, string Body, string? Authorization)> Requests { get; } = [];
        public Exception? Error { get; set; }
        public Task? Espera { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Espera is not null) await Espera;
            Requests.Add((request.RequestUri!.AbsolutePath,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString()));
            if (Error is not null) throw Error;
            return Respuestas.Dequeue();
        }
    }
}
