using Microsoft.Extensions.Logging.Abstractions;
using Plataforma.Mobile.Models;
using Plataforma.Mobile.Services;
using Plataforma.Mobile.ViewModels;
using Plataforma.Mobile.Data;
using EstadoBulto = Plataforma.Domain.EstadoBulto;

namespace Plataforma.Mobile.Tests;

public sealed class RecorridoDemoTests : IDisposable
{
    private readonly string directory = CrearDirectorio();

    private static string CrearDirectorio()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Plataforma.Mobile.Tests", Guid.NewGuid().ToString("N"));
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
        await vm.GuardarEntregaCommand.ExecuteAsync(null);
        Assert.True(vm.HaySeleccion);
        Assert.Contains("nombre", vm.Mensaje);
        vm.NombreReceptor = "Receptor de prueba";
        await vm.GuardarEntregaCommand.ExecuteAsync(null);
        Assert.False(vm.HaySeleccion);
        Assert.True(vm.MostrarRuta);
        Assert.Contains("1 pendientes", vm.Resumen);
        await vm.SincronizarCommand.ExecuteAsync(null);
        Assert.Contains("Sin conexion", vm.Mensaje);
        await vm.CerrarSesionCommand.ExecuteAsync(null);
        Assert.Equal(0, navigation.Logins);
        Assert.True(service.HaySesion);
        vm.ConexionSimulada = true;
        await vm.SincronizarCommand.ExecuteAsync(null);
        Assert.Equal("Sincronizacion completada.", vm.Mensaje);
        await vm.CerrarSesionCommand.ExecuteAsync(null);
        Assert.Equal(1, navigation.Logins);
        Assert.False(service.HaySesion);
        Assert.Empty(vm.Envios);
    }

    [Fact]
    public async Task RecorridoAvanzaSoloTrasGuardarYVuelveAListaAlFinal()
    {
        var service = CrearSesion();
        var vm = new RutaViewModel(service, new NavegacionPrueba(), NullLogger<RutaViewModel>.Instance);
        vm.CargarCommand.Execute(null);
        Assert.Equal(vm.Envios.Count, vm.EnviosVisibles.Count);
        vm.ConexionSimulada = false;
        vm.EmpezarRecorridoCommand.Execute(null);
        Assert.True(vm.RecorridoActivo);
        var primero = Assert.Single(vm.EnviosVisibles);
        vm.SeleccionarEntregaCommand.Execute(primero);
        await vm.GuardarEntregaCommand.ExecuteAsync(null);
        Assert.True(vm.HaySeleccion);
        Assert.Equal(primero.Id, Assert.Single(vm.EnviosVisibles).Id);
        vm.CancelarEntregaCommand.Execute(null);
        Assert.True(vm.RecorridoActivo);
        for (var i = 0; i < vm.Envios.Count; i++)
        {
            var actual = Assert.Single(vm.EnviosVisibles);
            vm.SeleccionarEntregaCommand.Execute(actual);
            vm.NombreReceptor = "Receptor";
            vm.IntentoFallido = i == 1;
            vm.Observaciones = i == 1 ? "Destinatario ausente" : "";
            await vm.GuardarEntregaCommand.ExecuteAsync(null);
        }
        Assert.False(vm.RecorridoActivo);
        Assert.Equal(vm.Envios.Count, vm.EnviosVisibles.Count);
        Assert.All(vm.Envios, e => Assert.Equal(EstadoSincronizacion.Pendiente, e.Sincronizacion));
    }

    [Fact]
    public async Task RecorridoPermiteAnteriorYPausaSinDuplicarNiPerderComprobantes()
    {
        var vm = new RutaViewModel(CrearSesion(), new NavegacionPrueba(), NullLogger<RutaViewModel>.Instance);
        vm.CargarCommand.Execute(null);
        vm.ConexionSimulada = false;
        vm.EmpezarRecorridoCommand.Execute(null);
        var primero = Assert.Single(vm.EnviosVisibles);
        vm.SeleccionarEntregaCommand.Execute(primero);
        vm.NombreReceptor = "Receptor";
        await vm.GuardarEntregaCommand.ExecuteAsync(null);
        var segundo = Assert.Single(vm.EnviosVisibles);
        Assert.NotEqual(primero.Id, segundo.Id);
        vm.ParadaAnteriorCommand.Execute(null);
        Assert.Equal(primero.Id, Assert.Single(vm.EnviosVisibles).Id);
        Assert.False(vm.EnviosVisibles[0].PuedeEntregar);
        vm.SiguienteParadaCommand.Execute(null);
        Assert.Equal(segundo.Id, Assert.Single(vm.EnviosVisibles).Id);
        vm.PausarRecorridoCommand.Execute(null);
        Assert.Equal(vm.Envios.Count, vm.EnviosVisibles.Count);
        vm.EmpezarRecorridoCommand.Execute(null);
        Assert.Equal(segundo.Id, Assert.Single(vm.EnviosVisibles).Id);
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
    public void HistorialDeEntregasPermiteFiltrarPorFecha()
    {
        var service = CrearSesion();
        var envio = service.ObtenerRuta().Envios[0];
        service.RegistrarEntrega(envio.Id, "Receptor", "Entregado en puerta");
        Assert.Equal(new ResultadoSincronizacion(1, 0, 0), service.Sincronizar());

        var viewModel = new RutaViewModel(service, new NavegacionPrueba(), NullLogger<RutaViewModel>.Instance);
        viewModel.AlternarMenuCommand.Execute(null);
        viewModel.AbrirHistorialCommand.Execute(null);

        Assert.True(viewModel.MostrarHistorial);
        Assert.False(viewModel.MenuAbierto);
        Assert.Equal(envio.NumeroSeguimiento, Assert.Single(viewModel.EntregasHistorial).NumeroSeguimiento);
        Assert.Equal("Mostrando 1 de 1 entrega", viewModel.ResumenHistorial);

        viewModel.TextoFechaHistorial = DateTime.Today.AddDays(-1).ToString("dd/MM/yyyy");
        Assert.Empty(viewModel.EntregasHistorial);
        viewModel.TextoFechaHistorial = DateTime.Today.ToString("dd/MM/yyyy");
        Assert.Single(viewModel.EntregasHistorial);
        viewModel.LimpiarFiltroHistorialCommand.Execute(null);
        Assert.Single(viewModel.EntregasHistorial);
    }

    [Fact]
    public void HistorialCargaDiezEntregasYLuegoLasSiguientesEnOrdenReciente()
    {
        var path = Path.Combine(directory, "historial.db");
        var data = new RepartidorLocalData(path);
        var service = new RepartidorDemoService(data);
        Assert.True(service.IniciarSesion("alvaro@demo.test", "Demo123!"));
        var estado = data.Leer();
        var envio = estado.Ruta!.Envios[0];
        var comprobantes = Enumerable.Range(0, 12).Select(indice => new EntregaPendiente(
            Guid.NewGuid(), envio.Id, $"Receptor {indice}", "", DateTimeOffset.Now.AddMinutes(-indice),
            EstadoEnvio.Entregado, NumeroSeguimiento: $"HIST-{indice:00}", Destinatario: "Destinatario de prueba"))
            .ToArray();
        data.Guardar(estado with { ComprobantesConfirmados = comprobantes });

        var viewModel = new RutaViewModel(service, new NavegacionPrueba(), NullLogger<RutaViewModel>.Instance);
        viewModel.AbrirHistorialCommand.Execute(null);

        Assert.Equal(10, viewModel.EntregasHistorial.Count);
        Assert.Equal("HIST-00", viewModel.EntregasHistorial[0].NumeroSeguimiento);
        Assert.Equal("HIST-09", viewModel.EntregasHistorial[^1].NumeroSeguimiento);
        viewModel.CargarMasHistorialCommand.Execute(null);
        Assert.Equal(12, viewModel.EntregasHistorial.Count);
        Assert.Equal("HIST-11", viewModel.EntregasHistorial[^1].NumeroSeguimiento);
    }

    [Fact]
    public void RutaMuestraAlFinalEntregadosYPendientesDeSincronizar()
    {
        var service = CrearSesion();
        var data = new RepartidorLocalData(Path.Combine(directory, "demo.db"));
        var estado = data.Leer();
        var ruta = estado.Ruta!;
        var envios = ruta.Envios.Select((envio, indice) => indice switch
        {
            0 => envio with { Estado = EstadoEnvio.Entregado },
            1 => envio with
            {
                Sincronizacion = EstadoSincronizacion.Pendiente,
                Entrega = new EntregaPendiente(Guid.NewGuid(), envio.Id, "Receptor", "", DateTimeOffset.Now)
            },
            _ => envio
        }).ToArray();
        data.Guardar(estado with { Ruta = ruta with { Envios = envios } });

        var viewModel = new RutaViewModel(new RepartidorDemoService(data), new NavegacionPrueba(), NullLogger<RutaViewModel>.Instance);
        viewModel.CargarCommand.Execute(null);

        Assert.Equal([envios[2].Id, envios[0].Id, envios[1].Id], viewModel.Envios.Select(envio => envio.Id));
    }

    [Fact]
    public void EntregaParcialGuardaResultadosPorBultoYMotivoEnLaColaOffline()
    {
        var service = CrearSesion();
        var estado = service.ObtenerRuta();
        var envio = estado.Envios[0];
        var bultos = new[]
        {
            new BultoEnvio(Guid.NewGuid(), "PKG-1", 1, EstadoBulto.EnRuta),
            new BultoEnvio(Guid.NewGuid(), "PKG-2", 1, EstadoBulto.EnRuta)
        };
        var actualizado = estado with
        {
            Envios = estado.Envios.Select(item => item.Id == envio.Id ? item with { Bultos = bultos } : item).ToArray()
        };
        var data = new RepartidorLocalData(Path.Combine(directory, "demo.db"));
        data.Guardar(data.Leer() with
        {
            Ruta = actualizado with
            {
                Funcionalidades = ["entrega-parcial"],
                AdmiteEntregaParcial = true,
                MotivosNoEntrega = ["Destinatario ausente"]
            }
        });
        var vm = new RutaViewModel(new RepartidorDemoService(data), new NavegacionPrueba(), NullLogger<RutaViewModel>.Instance);
        vm.CargarCommand.Execute(null);
        vm.ConexionSimulada = false;
        vm.SeleccionarEntregaCommand.Execute(vm.Envios.Single(item => item.Id == envio.Id));
        Assert.True(vm.UsaEntregaParcial);
        Assert.Equal(2, vm.BultosEntrega.Count);
        vm.NombreReceptor = "Receptor de prueba";
        vm.RegistrarBultoEntregado(bultos[0].Id, "CODIGO-INCORRECTO");
        Assert.False(vm.BultosEntrega.Single(item => item.Bulto.Id == bultos[0].Id).Entregado);
        vm.RegistrarBultoEntregado(bultos[0].Id, "PKG-1");
        var entregado = vm.BultosEntrega.Single(item => item.Bulto.Id == bultos[0].Id);
        Assert.True(entregado.Entregado);
        Assert.False(entregado.PuedeEscanear);

        var servicioReiniciado = new RepartidorDemoService(new RepartidorLocalData(Path.Combine(directory, "demo.db")));
        var viewModelReiniciado = new RutaViewModel(
            servicioReiniciado, new NavegacionPrueba(), NullLogger<RutaViewModel>.Instance);
        viewModelReiniciado.CargarCommand.Execute(null);
        viewModelReiniciado.SeleccionarEntregaCommand.Execute(
            viewModelReiniciado.Envios.Single(item => item.Id == envio.Id));
        var bultoRestaurado = viewModelReiniciado.BultosEntrega.Single(item => item.Bulto.Id == bultos[0].Id);
        Assert.True(bultoRestaurado.Entregado);
        Assert.False(bultoRestaurado.PuedeEscanear);

        vm.BultosEntrega.Single(item => item.Bulto.Id == bultos[1].Id).MotivoNoEntrega = "Destinatario ausente";

        vm.GuardarEntregaCommand.Execute(null);

        var guardado = new RepartidorLocalData(Path.Combine(directory, "demo.db")).Leer().Ruta!
            .Envios.Single(item => item.Id == envio.Id);
        Assert.Equal(EstadoEnvio.EntregadoParcialmente, guardado.Entrega!.Estado);
        Assert.Equal(EstadoSincronizacion.Pendiente, guardado.Sincronizacion);
        Assert.Collection(guardado.Entrega.Bultos!,
            entregado => Assert.Equal(EstadoBulto.Entregado, entregado.Estado),
            pendiente =>
            {
                Assert.Equal(EstadoBulto.NoEntregado, pendiente.Estado);
                Assert.Equal("Destinatario ausente", pendiente.MotivoNoEntrega);
            });
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
        Assert.Contains("No pudimos abrir tus datos guardados", error.Message);
    }

    [Fact]
    public void BaseCorruptaSeInformaSinReemplazarla()
    {
        var path = Path.Combine(directory, "demo.db");
        File.WriteAllText(path, "no es una base SQLite");
        var error = Assert.Throws<InvalidOperationException>(() => CrearServicio().ObtenerRuta());
        Assert.Contains("No pudimos abrir tus datos guardados", error.Message);
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
