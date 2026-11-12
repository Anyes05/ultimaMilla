using System.Net;
using System.Diagnostics;
using System.Text;
using Plataforma.Mobile.Data;
using Plataforma.Mobile.Models;
using Plataforma.Mobile.Services;
using EstadoBulto = Plataforma.Domain.EstadoBulto;

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

    private Plataforma.Mobile.ViewModels.RutaViewModel CrearRutaViewModel()
    {
        var vm = new Plataforma.Mobile.ViewModels.RutaViewModel(service, new NavegacionSesion(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Plataforma.Mobile.ViewModels.RutaViewModel>.Instance);
        vm.PantallaActiva = true;
        vm.CargarCommand.Execute(null);
        return vm;
    }

    [Fact]
    public async Task DespachoPosteriorNoSeMezclaConLaRutaEnCurso()
    {
        await Login();
        var contrato = ContratosRepartidor.Leer<RutaContrato[]>(
            ContratosRepartidor.LeerEjemplo("rutas-hoy.response.json"))[0];
        var activa = contrato with
        {
            IniciadaEn = DateTimeOffset.UtcNow,
            Envios = [contrato.Envios[0] with { Estado = EstadoEnvio.EnTransito }]
        };
        var siguiente = contrato with
        {
            Id = Guid.NewGuid(),
            Envios = [contrato.Envios[0] with { Id = Guid.NewGuid(), Estado = EstadoEnvio.AsignadoARuta }]
        };
        handler.Respuestas.Enqueue(Ok(new[] { siguiente, activa }));

        await service.DescargarRutaAsync();

        Assert.Equal(activa.Id, service.ObtenerRuta().Id);
        Assert.Equal(activa.Envios[0].Id, Assert.Single(service.ObtenerRuta().Envios).Id);
        handler.Respuestas.Enqueue(Ok(new[] { activa with
        {
            Envios = [activa.Envios[0] with { Estado = EstadoEnvio.Entregado }]
        }, siguiente }));
        await service.DescargarRutaAsync();
        Assert.Equal(siguiente.Id, service.ObtenerRuta().Id);
        Assert.Null(service.ObtenerRuta().IniciadaEn);
    }

    [Fact]
    public async Task IniciarRutaSolicitaConfirmacionSinAdelantarElEstadoLocal()
    {
        await Login();
        var ruta = service.ObtenerRuta();
        handler.Respuestas.Enqueue(Ok(new { iniciados = 1, errores = Array.Empty<string>() }));

        await service.IniciarRutaAsync();

        Assert.Equal($"/api/repartidor/rutas/{ruta.Id}/iniciar", handler.Requests.Last().Path);
        Assert.Equal(ruta, service.ObtenerRuta());
    }

    [Fact]
    public async Task ActualizarRutaIncorporaDosEnviosDespachados()
    {
        await Login();
        var vm = CrearRutaViewModel();
        var ruta = ContratosRepartidor.Leer<RutaContrato[]>(ContratosRepartidor.LeerEjemplo("rutas-hoy.response.json"))[0];
        var nuevos = new[] {
            ruta.Envios[0] with { Id = Guid.NewGuid(), NumeroSeguimiento = "NUEVO-1" },
            ruta.Envios[0] with { Id = Guid.NewGuid(), NumeroSeguimiento = "NUEVO-2" }
        };
        handler.Respuestas.Enqueue(Ok(new[] { ruta with { Envios = ruta.Envios.Concat(nuevos).ToArray() } }));

        await vm.DescargarRutaCommand.ExecuteAsync(null);

        Assert.Equal("/api/repartidor/rutas/hoy", handler.Requests.Last().Path);
        Assert.Equal(5, vm.EnviosVisibles.Count);
        Assert.All(nuevos, nuevo => Assert.Contains(vm.EnviosVisibles, e => e.Id == nuevo.Id));
        Assert.Equal("Ruta actualizada.", vm.Mensaje);
        Assert.False(vm.Ocupado);
    }

    [Fact]
    public async Task DescargarRutaMapeaBanderasReglasYBultos()
    {
        await Login();
        var rutaOriginal = ContratosRepartidor.Leer<RutaContrato[]>(
            ContratosRepartidor.LeerEjemplo("rutas-hoy.response.json"))[0];
        var envio = rutaOriginal.Envios[0] with
        {
            Bultos =
            [
                new BultoContrato(Guid.NewGuid(), "PKG-1", 1, EstadoBulto.EnRuta),
                new BultoContrato(Guid.NewGuid(), "PKG-2", 1, EstadoBulto.EnRuta)
            ]
        };
        var operador = rutaOriginal.Operador with
        {
            Funcionalidades = ["entrega-parcial"],
            AdmiteEntregaParcial = true,
            ExigeFirma = true,
            ExigeDocumentoReceptor = true,
            MotivosNoEntrega = ["Destinatario ausente"]
        };
        handler.Respuestas.Enqueue(Ok(new[] { rutaOriginal with { Operador = operador, Envios = [envio] } }));

        await service.DescargarRutaAsync();

        var ruta = service.ObtenerRuta();
        Assert.True(ruta.EntregaParcialHabilitada);
        Assert.True(ruta.ExigeFirma);
        Assert.True(ruta.ExigeDocumentoReceptor);
        Assert.Contains("Destinatario ausente", ruta.MotivosNoEntrega!);
        Assert.Equal(2, Assert.Single(ruta.Envios).Bultos!.Count);
    }

    [Fact]
    public async Task DocumentoReceptorViajaConElComprobanteSincronizado()
    {
        await Login();
        var envio = service.ObtenerRuta().Envios[0];
        service.RegistrarEvento(new EntregaPendiente(Guid.NewGuid(), envio.Id, "Receptor", "",
            DateTimeOffset.Now, DocumentoReceptor: "12345678"));
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta(
            [new AplicadoContrato(envio.Id, EstadoEnvio.Entregado, 0)], [])));

        await service.SincronizarAsync();

        var solicitud = ContratosRepartidor.Leer<SincronizacionSolicitud>(handler.Requests.Last().Body);
        Assert.Equal("12345678", Assert.Single(solicitud.Eventos).DocumentoReceptor);
        Assert.Equal("12345678", Assert.Single(service.ObtenerHistorialEntregas()).DocumentoReceptor);
    }

    [Fact]
    public async Task RetiroOfflineConservaEvidenciaAlCerrarSesion()
    {
        await Login();
        var ruta = service.ObtenerRuta();
        var retiro = new RetiroEnRuta(Guid.NewGuid(), ruta.Id, ruta.Fecha, "Direccion", "Contacto", 1, 0,
            Plataforma.Domain.EstadoRetiro.EnCurso);
        data.Guardar(data.Leer() with { Ruta = ruta with { Retiros = [retiro] } });
        service.ConexionSimulada = false;
        var comprobante = new RetiroPendiente(Guid.NewGuid(), retiro.Id, retiro.RutaId, DateTimeOffset.Now,
            ["BULTO-RETIRO"], "Responsable", "/9j/2Q==", "[[{\"x\":1,\"y\":1},{\"x\":2,\"y\":2}]]", -34.9, -56.16);

        service.RegistrarRetiro(comprobante);
        service.CerrarSesion();

        Assert.False(service.HaySesion);
        Assert.Null(tokens.Value);
        var guardado = Assert.Single(data.Leer().RetirosConComprobante!);
        Assert.Equal(EstadoSincronizacion.Pendiente, guardado.Sincronizacion);
        Assert.Equal(comprobante.EventoId, guardado.Comprobante!.EventoId);
        Assert.Equal(comprobante.Foto, guardado.Comprobante.Foto);
        Assert.Equal(comprobante.Firma, guardado.Comprobante.Firma);
    }

    [Fact]
    public async Task RetiroUsaContratoPorLoteYFechaDeLaRuta()
    {
        await Login();
        var contrato = ContratosRepartidor.Leer<RutaContrato[]>(
            ContratosRepartidor.LeerEjemplo("rutas-hoy.response.json"))[0];
        var retiroId = Guid.NewGuid();
        handler.Respuestas.Enqueue(Ok(new[] { contrato with
        {
            Retiros = [new RetiroContrato(retiroId, "Direccion", "Contacto", 1, 0,
                Plataforma.Domain.EstadoRetiro.EnCurso)]
        } }));
        await service.DescargarRutaAsync();
        var retiro = Assert.Single(service.ObtenerRetiros());
        Assert.Equal(contrato.Id, retiro.RutaId);
        Assert.Equal(contrato.Fecha, retiro.Fecha);
        service.RegistrarRetiro(new RetiroPendiente(Guid.NewGuid(), retiro.Id, retiro.RutaId,
            DateTimeOffset.Now, ["BULTO-RETIRO"], "Responsable", "/9j/2Q==", "firma", -34.9, -56.16));
        handler.Respuestas.Enqueue(Ok(new
        {
            aplicados = new[] { new { retiroId, estado = "Completado" } },
            rechazados = Array.Empty<object>()
        }));

        await service.SincronizarAsync();

        using var solicitud = System.Text.Json.JsonDocument.Parse(handler.Requests.Last().Body);
        Assert.Equal(contrato.Fecha.ToString("yyyy-MM-dd"), solicitud.RootElement.GetProperty("fecha").GetString());
        var evento = Assert.Single(solicitud.RootElement.GetProperty("eventos").EnumerateArray());
        Assert.Equal(retiroId, evento.GetProperty("retiroId").GetGuid());
        Assert.Equal("Retirado", evento.GetProperty("resultado").GetString());
        Assert.Equal("Responsable", evento.GetProperty("nombreEntrega").GetString());
        Assert.Equal(EstadoSincronizacion.Sincronizado, Assert.Single(service.ObtenerRetiros()).Sincronizacion);
    }

    [Fact]
    public async Task AlMostrarLaPaginaRefrescaLaRutaDeHoy()
    {
        await Login();
        var vm = CrearRutaViewModel();
        vm.SoloRutasDeHoy = true;
        var ruta = ContratosRepartidor.Leer<RutaContrato[]>(
            ContratosRepartidor.LeerEjemplo("rutas-hoy.response.json"))[0] with
        {
            Fecha = DateOnly.FromDateTime(DateTime.Today)
        };
        handler.Respuestas.Enqueue(Ok(new[] { ruta }));

        await vm.RefrescarRutaAlMostrarAsync();

        Assert.Equal("/api/repartidor/rutas/hoy", handler.Requests.Last().Path);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), service.ObtenerRuta().Fecha);
        Assert.Equal(ruta.Envios.Count, vm.EnviosVisibles.Count);
    }

    [Fact]
    public async Task AutosyncSinPendientesNoBorraResultadoDeActualizarRuta()
    {
        await Login();
        var vm = CrearRutaViewModel();
        vm.Mensaje = "Ruta actualizada.";
        var solicitudes = handler.Requests.Count;
        await vm.AutoSincronizarAsync();
        Assert.Equal(solicitudes, handler.Requests.Count);
        Assert.Equal("Ruta actualizada.", vm.Mensaje);
        Assert.False(vm.Ocupado);
    }

    [Fact]
    public async Task ActualizarDuranteSincronizacionInformaBloqueoYPermiteReintentar()
    {
        await Login();
        service.RegistrarEntrega(service.ObtenerRuta().Envios[0].Id, "Receptor", "");
        var vm = CrearRutaViewModel();
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Espera = liberar.Task;
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta(
            [new AplicadoContrato(vm.Envios[0].Id, EstadoEnvio.Entregado, 0)], [])));
        var sincronizacion = vm.AutoSincronizarAsync();
        Assert.True(vm.Ocupado);
        await vm.DescargarRutaCommand.ExecuteAsync(null);
        Assert.Contains("operacion en curso", vm.Mensaje);
        liberar.SetResult();
        await sincronizacion;
        handler.Respuestas.Enqueue(Ejemplo("rutas-hoy.response.json"));
        await vm.DescargarRutaCommand.ExecuteAsync(null);
        Assert.Equal("Ruta actualizada.", vm.Mensaje);
        Assert.False(vm.Ocupado);
    }

    [Fact]
    public async Task TokenQueNoRespondeLiberaActualizacionSinPerderDatos()
    {
        await Login();
        var vm = CrearRutaViewModel();
        var rutaAnterior = service.ObtenerRuta();
        var liberar = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        tokens.Lectura = liberar.Task;
        try
        {
            await vm.DescargarRutaCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(vm.Ocupado);
            Assert.Contains("sesion segura", vm.Mensaje);
            Assert.Equal(ContratosRepartidor.Escribir(rutaAnterior),
                ContratosRepartidor.Escribir(service.ObtenerRuta()));
            tokens.Lectura = null;
            handler.Respuestas.Enqueue(Ejemplo("rutas-hoy.response.json"));
            await vm.DescargarRutaCommand.ExecuteAsync(null);
            Assert.Equal("Ruta actualizada.", vm.Mensaje);
        }
        finally { liberar.TrySetResult(tokens.Value); }
    }

    [Fact]
    public async Task ActualizacionSinRedMuestraErrorYLiberaBotones()
    {
        await Login();
        var vm = CrearRutaViewModel();
        handler.Error = new HttpRequestException("Sin red");
        await vm.DescargarRutaCommand.ExecuteAsync(null);
        Assert.False(vm.Ocupado);
        Assert.Contains("Comprueba Internet", vm.Mensaje);
        Assert.Equal(3, vm.EnviosVisibles.Count);
    }

    [Fact]
    public async Task SolicitudesSinPadreCreanTraceparentW3C()
    {
        await Login();
        Assert.Equal(2, handler.Trazas.Count);
        foreach (var traza in handler.Trazas)
        {
            Assert.True(ActivityContext.TryParse(traza.Parent, traza.State, out var context));
            Assert.NotEqual(default, context.TraceId);
            Assert.NotEqual(default, context.SpanId);
        }
    }

    [Fact]
    public async Task SincronizacionContinuaTrazaSinReutilizarSpanNiFiltrarCredenciales()
    {
        await Login();
        using var padre = new Activity("Prueba").SetIdFormat(ActivityIdFormat.W3C).Start();
        padre.TraceStateString = "prueba=valor";
        var envio = service.ObtenerRuta().Envios[0];
        service.RegistrarEntrega(envio.Id, "Receptor", "");
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta(
            [new AplicadoContrato(envio.Id, EstadoEnvio.Entregado, 0)], [])));
        await service.SincronizarAsync();
        var traza = handler.Trazas.Last();
        Assert.True(ActivityContext.TryParse(traza.Parent, traza.State, out var contexto));
        Assert.Equal(padre.TraceId, contexto.TraceId);
        Assert.NotEqual(padre.SpanId, contexto.SpanId);
        Assert.Equal("prueba=valor", traza.State);
        Assert.Same(padre, Activity.Current);
        Assert.DoesNotContain(tokens.Value!, traza.Parent);
    }

    [Fact]
    public async Task ViewModelAbreAccesoUnaVezYDetieneAutosyncTras401()
    {
        await Login();
        service.RegistrarEntrega(service.ObtenerRuta().Envios[0].Id, "Receptor", "");
        var navigation = new NavegacionSesion();
        var vm = new Plataforma.Mobile.ViewModels.RutaViewModel(service, navigation,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Plataforma.Mobile.ViewModels.RutaViewModel>.Instance);
        vm.PantallaActiva = true;
        vm.CargarCommand.Execute(null);
        handler.Respuestas.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await vm.SincronizarCommand.ExecuteAsync(null);
        Assert.Equal(1, navigation.Accesos);
        Assert.False(vm.Ocupado);
        var cantidad = handler.Requests.Count;
        await vm.AutoSincronizarAsync();
        Assert.Equal(cantidad, handler.Requests.Count);
        Assert.Equal(1, navigation.Accesos);
        Assert.NotNull(service.ObtenerRuta().Envios[0].Entrega);
    }

    [Fact]
    public async Task PermisoDenegadoNoInvalidaSesion()
    {
        await Login();
        service.RegistrarEntrega(service.ObtenerRuta().Envios[0].Id, "Receptor", "");
        handler.Respuestas.Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SincronizarAsync());
        Assert.True(service.HaySesion);
        Assert.False(service.RequiereAutenticacion);
    }

    private sealed class NavegacionSesion : INavigationService
    {
        public int Accesos { get; private set; }
        public Task MostrarLoginAsync() { Accesos++; return Task.CompletedTask; }
        public Task MostrarRutaAsync() => Task.CompletedTask;
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
    }

    [Fact]
    public async Task CerrarSesionConservaRechazosYSuEvidencia()
    {
        await RechazoConservaComprobanteYEstadoServidor();
        var comprobante = service.ObtenerRuta().Envios[0].Entrega;
        service.CerrarSesion();
        Assert.False(service.HaySesion);
        Assert.Equal(comprobante, data.Leer().Ruta!.Envios[0].Entrega);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(5)]
    public async Task PendienteExistenteSeEnviaEnUtcSinModificarComprobante(int offsetHoras)
    {
        await Login();
        var envio = service.ObtenerRuta().Envios[0];
        var comprobante = new EntregaPendiente(Guid.NewGuid(), envio.Id, "Receptor", "Puerta",
            new DateTimeOffset(2026, 11, 5, 10, 26, 0, TimeSpan.FromHours(offsetHoras)));
        service.RegistrarEvento(comprobante);
        var reopened = new RepartidorHttpService(http,
            new RepartidorLocalData(Path.Combine(directory, "api.db")), tokens);
        handler.Respuestas.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.SincronizarAsync());
        var enviado = Assert.Single(ContratosRepartidor.Leer<SincronizacionSolicitud>(
            handler.Requests.Last().Body).Eventos);
        Assert.Equal(TimeSpan.Zero, enviado.FechaDispositivo.Offset);
        Assert.Equal(comprobante.Fecha.UtcDateTime, enviado.FechaDispositivo.UtcDateTime);
        Assert.Equal(comprobante.Id, enviado.EventoId);
        Assert.Equal(comprobante, reopened.ObtenerRuta().Envios[0].Entrega);
        Assert.Equal(comprobante.Fecha.Offset, reopened.ObtenerRuta().Envios[0].Entrega!.Fecha.Offset);

        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta(
            [new AplicadoContrato(envio.Id, EstadoEnvio.Entregado, 0)], [])));
        Assert.Equal(new ResultadoSincronizacion(1, 0, 0), await reopened.SincronizarAsync());
        var reintentado = Assert.Single(ContratosRepartidor.Leer<SincronizacionSolicitud>(
            handler.Requests.Last().Body).Eventos);
        Assert.Equal(enviado, reintentado);
        Assert.Equal(comprobante, reopened.ObtenerRuta().Envios[0].Entrega);
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
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.SincronizarAsync());
        Assert.Equal(EstadoSincronizacion.Pendiente, service.ObtenerRuta().Envios[0].Sincronizacion);
    }

    [Fact]
    public async Task SesionInvalidaPersisteYReloginConservaPendienteSinDescargarOtraRuta()
    {
        await Login();
        var envio = service.ObtenerRuta().Envios[0];
        service.RegistrarEntrega(envio.Id, "Receptor", "");
        var comprobante = service.ObtenerRuta().Envios[0].Entrega;
        handler.Respuestas.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<SesionRepartidorVencidaException>(() => service.SincronizarAsync());
        Assert.False(service.HaySesion);
        Assert.True(service.RequiereAutenticacion);
        var reopened = new RepartidorHttpService(http,
            new RepartidorLocalData(Path.Combine(directory, "api.db")), tokens);
        Assert.False(reopened.HaySesion);
        var cantidad = handler.Requests.Count;
        await Assert.ThrowsAsync<SesionRepartidorVencidaException>(() => reopened.DescargarRutaAsync());
        Assert.Equal(cantidad, handler.Requests.Count);
        handler.Respuestas.Enqueue(Ejemplo("login.response.json"));
        Assert.True(await reopened.IniciarSesionAsync("alvaro@demo.test", "Demo123!"));
        Assert.Equal(cantidad + 1, handler.Requests.Count);
        Assert.True(reopened.HaySesion);
        Assert.False(reopened.RequiereAutenticacion);
        Assert.Equal(comprobante, reopened.ObtenerRuta().Envios[0].Entrega);
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta(
            [new AplicadoContrato(envio.Id, EstadoEnvio.Entregado, 0)], [])));
        await reopened.SincronizarAsync();
        Assert.Equal(comprobante!.Id,
            ContratosRepartidor.Leer<SincronizacionSolicitud>(handler.Requests.Last().Body).Eventos[0].EventoId);
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
    public async Task CambiarCuentaTrasSalirConservaPendientesSinMezclarlos()
    {
        await Login();
        var sesionOriginal = ContratosRepartidor.Leer<LoginRespuesta>(ContratosRepartidor.LeerEjemplo("login.response.json"));
        service.RegistrarEntrega(service.ObtenerRuta().Envios[0].Id, "Receptor", "");
        var comprobante = service.ObtenerRuta().Envios[0].Entrega;
        service.CerrarSesion();
        Assert.False(service.HaySesion);
        Assert.Null(tokens.Value);
        handler.Respuestas.Enqueue(Ok(sesionOriginal with
        {
            OperadorId = Guid.NewGuid(), RepartidorId = Guid.NewGuid(), Token = "otra-cuenta"
        }));
        handler.Respuestas.Enqueue(Ok(Array.Empty<RutaContrato>()));

        Assert.True(await service.IniciarSesionAsync("otra-cuenta", "clave"));
        Assert.Empty(service.ObtenerRuta().Envios);
        Assert.Equal(0, service.ObtenerCantidadSincronizacionesPendientes());
        service.CerrarSesion();
        handler.Respuestas.Enqueue(Ok(sesionOriginal));

        Assert.True(await service.IniciarSesionAsync("original", "clave"));
        Assert.Equal(comprobante, service.ObtenerRuta().Envios[0].Entrega);
        Assert.Equal(1, service.ObtenerCantidadSincronizacionesPendientes());
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
        var rutaAnterior = service.ObtenerRuta();
        var envioPendiente = rutaAnterior.Envios[0];
        service.RegistrarEntrega(envioPendiente.Id, "Receptor", "");

        var rutaHoy = ruta with { Id = Guid.NewGuid(), Fecha = DateOnly.FromDateTime(DateTime.Today) };
        handler.Respuestas.Enqueue(Ok(new[] { rutaHoy }));
        await service.DescargarRutaAsync();

        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), service.ObtenerRuta().Fecha);
        var archivada = Assert.Single(data.Leer().RutasPendientes!);
        Assert.Equal(envioPendiente.Id, Assert.Single(archivada.Envios).Id);
        Assert.Equal(1, service.ObtenerCantidadSincronizacionesPendientes());

        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta(
            [new AplicadoContrato(envioPendiente.Id, EstadoEnvio.Entregado, 1)], [])));
        // Con algo aplicado, la app vuelve a bajar la ruta (ca9673a).
        handler.Respuestas.Enqueue(Ok(new[] { rutaHoy }));
        var viewModel = CrearRutaViewModel();
        await viewModel.AutoSincronizarAsync();
        var sincronizacion = ContratosRepartidor.Leer<SincronizacionSolicitud>(
            handler.Requests.Last(request => request.Path == "/api/repartidor/sincronizacion").Body);
        Assert.Equal(rutaAnterior.Fecha, sincronizacion.Fecha);
        Assert.Equal(envioPendiente.Id, Assert.Single(sincronizacion.Eventos).EnvioId);
        Assert.Empty(data.Leer().RutasPendientes!);
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
        public Task<string?>? Lectura { get; set; }
        public Task<string?> LeerAsync() => Lectura ?? Task.FromResult(Value);
        public Task GuardarAsync(string token) { Value = token; return Task.CompletedTask; }
        public void Borrar() => Value = null;
    }

    private sealed class Servidor : HttpMessageHandler
    {
        public Queue<HttpResponseMessage> Respuestas { get; } = new();
        public List<(string Path, string Body, string? Authorization)> Requests { get; } = [];
        public List<(string Parent, string? State)> Trazas { get; } = [];
        public Exception? Error { get; set; }
        public Task? Espera { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Espera is not null) await Espera;
            Trazas.Add((request.Headers.GetValues("traceparent").Single(),
                request.Headers.TryGetValues("tracestate", out var estados) ? estados.Single() : null));
            Requests.Add((request.RequestUri!.AbsolutePath,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString()));
            if (Error is not null) throw Error;
            return Respuestas.Dequeue();
        }
    }
}
