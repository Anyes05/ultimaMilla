using Microsoft.Extensions.Logging.Abstractions;
using Plataforma.Mobile.Models;
using Plataforma.Mobile.Services;
using Plataforma.Mobile.ViewModels;
using Plataforma.Mobile.Data;

namespace Plataforma.Mobile.Tests;

public sealed class RecorridoDemoTests : IDisposable
{
    private readonly string directory = CrearDirectorio();

    private static string CrearDirectorio()
    {
        var path = Path.Combine(Path.GetTempPath(), "Plataforma.Mobile.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private RepartidorDemoService CrearServicio() =>
        new(new RepartidorLocalData(Path.Combine(directory, "demo.db")));

    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void ExigeSesionParaConsultarRegistrarYSincronizar()
    {
        var service = CrearServicio();
        Assert.Throws<InvalidOperationException>(() => service.ObtenerRuta());
        Assert.Throws<InvalidOperationException>(() => service.RegistrarEntrega(Guid.NewGuid(), "Prueba", ""));
        Assert.Throws<InvalidOperationException>(() => service.Sincronizar());
    }

    [Fact]
    public async Task LoginValidaCredencialesYNavegaSoloAlIngresar()
    {
        var service = CrearServicio();
        var navigation = new NavegacionPrueba();
        var vm = new LoginViewModel(service, navigation, NullLogger<LoginViewModel>.Instance);

        await vm.IngresarCommand.ExecuteAsync(null);
        Assert.False(service.HaySesion);
        Assert.NotEmpty(vm.Mensaje);
        vm.Clave = "incorrecta";
        await vm.IngresarCommand.ExecuteAsync(null);
        Assert.False(service.HaySesion);
        Assert.Equal(0, navigation.Rutas);

        vm.Usuario = " ALVARO@DEMO.TEST ";
        vm.Clave = "Demo123!";
        await vm.IngresarCommand.ExecuteAsync(null);
        Assert.True(service.HaySesion);
        Assert.Equal(1, navigation.Rutas);
        Assert.Empty(vm.Clave);
        Assert.False(vm.Ocupado);
    }

    [Fact]
    public void NoPermiteLoginSinConexionSimulada()
    {
        var service = CrearServicio();
        service.ConexionSimulada = false;
        Assert.Throws<InvalidOperationException>(() => service.IniciarSesion("alvaro@demo.test", "Demo123!"));
        Assert.False(service.HaySesion);
    }

    [Fact]
    public void EntregaSinConexionSeConservaYNoDuplica()
    {
        var service = CrearSesion();
        var original = service.ObtenerRuta().Envios[0];
        service.ConexionSimulada = false;
        service.RegistrarEntrega(original.Id, " Receptor ficticio ", " Puerta ");
        var entrega = service.ObtenerRuta().Envios[0];

        Assert.Equal(EstadoSincronizacion.Pendiente, entrega.Sincronizacion);
        Assert.Equal(EstadoEnvio.EnTransito, entrega.Estado);
        Assert.Equal("Receptor ficticio", entrega.Entrega!.NombreReceptor);
        Assert.Equal("Puerta", entrega.Entrega.Observaciones);
        Assert.False(entrega.PuedeEntregar);
        Assert.Throws<InvalidOperationException>(() => service.RegistrarEntrega(original.Id, "Otra", ""));
        Assert.Throws<InvalidOperationException>(() => service.Sincronizar());
        Assert.Equal(entrega, service.ObtenerRuta().Envios[0]);

        service.ConexionSimulada = true;
        Assert.Equal(new ResultadoSincronizacion(1, 0, 0), service.Sincronizar());
        Assert.Equal(EstadoEnvio.Entregado, service.ObtenerRuta().Envios[0].Estado);
        Assert.Equal(new ResultadoSincronizacion(0, 0, 0), service.Sincronizar());
    }

    [Fact]
    public void RechazosConservanComprobantesYEstadoAutoritativo()
    {
        var service = CrearSesion();
        foreach (var envio in service.ObtenerRuta().Envios)
            service.RegistrarEntrega(envio.Id, "Receptor de prueba", "Observacion local");
        var comprobantes = service.ObtenerRuta().Envios.Select(item => item.Entrega).ToArray();

        Assert.Equal(new ResultadoSincronizacion(1, 2, 2), service.Sincronizar());
        var envios = service.ObtenerRuta().Envios;
        Assert.Equal(EstadoSincronizacion.Sincronizado, envios[0].Sincronizacion);
        Assert.Equal(EstadoEnvio.Entregado, envios[1].Estado);
        Assert.Equal(EstadoEnvio.Extraviado, envios[2].Estado);
        foreach (var index in new[] { 1, 2 })
        {
            Assert.Equal(EstadoSincronizacion.Rechazado, envios[index].Sincronizacion);
            Assert.NotEmpty(envios[index].MotivoRechazo!);
            Assert.Equal(comprobantes[index], envios[index].Entrega);
        }
        Assert.Equal(new ResultadoSincronizacion(0, 2, 2), service.Sincronizar());
        Assert.Throws<InvalidOperationException>(() => service.CerrarSesion());
        Assert.True(service.HaySesion);
    }

    [Fact]
    public void ValidaNombreLongitudesYPertenenciaSinCambiarLaCola()
    {
        var service = CrearSesion();
        var envio = service.ObtenerRuta().Envios[0];
        Assert.Throws<InvalidOperationException>(() => service.RegistrarEntrega(envio.Id, " ", ""));
        Assert.Throws<InvalidOperationException>(() => service.RegistrarEntrega(envio.Id, new string('x', 101), ""));
        Assert.Throws<InvalidOperationException>(() => service.RegistrarEntrega(envio.Id, "Prueba", new string('x', 501)));
        Assert.Throws<InvalidOperationException>(() => service.RegistrarEntrega(Guid.NewGuid(), "Prueba", ""));
        Assert.All(service.ObtenerRuta().Envios, item => Assert.Null(item.Entrega));
    }

    [Fact]
    public async Task ViewModelCompletaRecorridoYExponeErroresSinPerderDatos()
    {
        var service = CrearSesion();
        var navigation = new NavegacionPrueba();
        var vm = new RutaViewModel(service, navigation, NullLogger<RutaViewModel>.Instance);
        vm.CargarCommand.Execute(null);
        Assert.Equal(3, vm.Envios.Count);
        Assert.True(vm.MostrarRuta);
        vm.ConexionSimulada = false;
        vm.SeleccionarEntregaCommand.Execute(vm.Envios[0]);
        Assert.False(vm.MostrarRuta);
        vm.GuardarEntregaCommand.Execute(null);
        Assert.True(vm.HaySeleccion);
        Assert.Contains("nombre", vm.Mensaje);
        vm.NombreReceptor = "Receptor de prueba";
        vm.GuardarEntregaCommand.Execute(null);
        Assert.False(vm.HaySeleccion);
        Assert.True(vm.MostrarRuta);
        Assert.Contains("1 pendientes", vm.Resumen);
        vm.SincronizarCommand.Execute(null);
        Assert.Contains("Sin conexion", vm.Mensaje);
        await vm.CerrarSesionCommand.ExecuteAsync(null);
        Assert.Equal(0, navigation.Logins);
        Assert.True(service.HaySesion);
        vm.ConexionSimulada = true;
        vm.SincronizarCommand.Execute(null);
        Assert.Contains("1 aceptadas", vm.Mensaje);
        await vm.CerrarSesionCommand.ExecuteAsync(null);
        Assert.Equal(1, navigation.Logins);
        Assert.False(service.HaySesion);
        Assert.Empty(vm.Envios);
    }

    [Fact]
    public void CerrarSesionSinCambiosPermiteNuevaDemoLimpia()
    {
        var service = CrearSesion();
        var ruta = service.ObtenerRuta();
        service.ConexionSimulada = false;
        service.CerrarSesion();
        Assert.False(service.HaySesion);
        Assert.True(service.ConexionSimulada);
        Assert.True(service.IniciarSesion("alvaro@demo.test", "Demo123!"));
        Assert.Equal(ruta.Id, service.ObtenerRuta().Id);
        Assert.All(service.ObtenerRuta().Envios, envio => Assert.Null(envio.Entrega));
    }

    [Fact]
    public async Task ReinicioSinRedRecuperaRutaYColaDesdeSQLite()
    {
        var service = CrearSesion();
        var ruta = service.ObtenerRuta();
        service.ConexionSimulada = false;
        service.RegistrarEntrega(ruta.Envios[0].Id, "Receptor offline", "Prueba de reinicio");
        var comprobante = service.ObtenerRuta().Envios[0].Entrega;

        var reiniciado = CrearServicio();
        reiniciado.ConexionSimulada = false;
        var navigation = new NavegacionPrueba();
        var login = new LoginViewModel(reiniciado, navigation, NullLogger<LoginViewModel>.Instance);
        await login.ContinuarSesionCommand.ExecuteAsync(null);
        Assert.Equal(1, navigation.Rutas);
        Assert.Empty(login.Clave);
        Assert.True(reiniciado.HaySesion);
        var recuperada = reiniciado.ObtenerRuta();
        Assert.Equal(ruta.Id, recuperada.Id);
        Assert.Equal(comprobante, recuperada.Envios[0].Entrega);
        Assert.Equal(EstadoSincronizacion.Pendiente, recuperada.Envios[0].Sincronizacion);
        Assert.Throws<InvalidOperationException>(() => reiniciado.Sincronizar());

        reiniciado.ConexionSimulada = true;
        Assert.Equal(new ResultadoSincronizacion(1, 0, 0), reiniciado.Sincronizar());
        var segundoReinicio = CrearServicio();
        Assert.Equal(EstadoSincronizacion.Sincronizado, segundoReinicio.ObtenerRuta().Envios[0].Sincronizacion);
        Assert.Equal(comprobante, segundoReinicio.ObtenerRuta().Envios[0].Entrega);
        Assert.Equal(new ResultadoSincronizacion(0, 0, 0), segundoReinicio.Sincronizar());
    }

    [Fact]
    public void ReinicioConservaRechazosYNoCreaUnaRutaNuevaAlIngresar()
    {
        var service = CrearSesion();
        var ruta = service.ObtenerRuta();
        service.RegistrarEntrega(ruta.Envios[1].Id, "Receptor ficticio", "");
        service.Sincronizar();
        var rechazado = service.ObtenerRuta().Envios[1];

        var reiniciado = CrearServicio();
        Assert.True(reiniciado.IniciarSesion("alvaro@demo.test", "Demo123!"));
        Assert.Equal(ruta.Id, reiniciado.ObtenerRuta().Id);
        Assert.Equal(rechazado, reiniciado.ObtenerRuta().Envios[1]);
        Assert.Throws<InvalidOperationException>(() => reiniciado.CerrarSesion());
        Assert.True(CrearServicio().HaySesion);
    }

    [Fact]
    public async Task ContinuarSinSesionInformaErrorSinNavegar()
    {
        var navigation = new NavegacionPrueba();
        var login = new LoginViewModel(CrearServicio(), navigation, NullLogger<LoginViewModel>.Instance);
        await login.ContinuarSesionCommand.ExecuteAsync(null);
        Assert.Equal(0, navigation.Rutas);
        Assert.Contains("No hay una sesion", login.Mensaje);
        Assert.False(login.Ocupado);
    }

    [Fact]
    public void ErrorSQLiteNoSePresentaComoGuardadoExitoso()
    {
        var data = new RepartidorLocalData(Path.Combine(directory, "carpeta-inexistente", "demo.db"));
        var service = new RepartidorDemoService(data);
        var error = Assert.Throws<InvalidOperationException>(() => service.IniciarSesion("alvaro@demo.test", "Demo123!"));
        Assert.Contains("SQLite", error.Message);
    }

    [Fact]
    public void BaseCorruptaSeInformaSinReemplazarla()
    {
        var path = Path.Combine(directory, "demo.db");
        File.WriteAllText(path, "no es una base SQLite");
        var error = Assert.Throws<InvalidOperationException>(() => CrearServicio().ObtenerRuta());
        Assert.Contains("SQLite", error.Message);
        Assert.Equal("no es una base SQLite", File.ReadAllText(path));
    }

    [Fact]
    public void CierreDeSesionConfirmadoPersisteTrasReiniciar()
    {
        var service = CrearSesion();
        service.CerrarSesion();
        Assert.False(CrearServicio().HaySesion);
        Assert.Throws<InvalidOperationException>(() => CrearServicio().ObtenerRuta());
    }

    private RepartidorDemoService CrearSesion()
    {
        var service = CrearServicio();
        Assert.True(service.IniciarSesion("alvaro@demo.test", "Demo123!"));
        return service;
    }

    private sealed class NavegacionPrueba : INavigationService
    {
        public int Rutas { get; private set; }
        public int Logins { get; private set; }

        public Task MostrarRutaAsync()
        {
            Rutas++;
            return Task.CompletedTask;
        }

        public Task MostrarLoginAsync()
        {
            Logins++;
            return Task.CompletedTask;
        }
    }
}
