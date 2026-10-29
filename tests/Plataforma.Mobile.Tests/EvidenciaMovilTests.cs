using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Plataforma.Mobile.Data;
using Plataforma.Mobile.Models;
using Plataforma.Mobile.Services;
using Plataforma.Mobile.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Plataforma.Mobile.Tests;

public sealed partial class RepartidorHttpTests
{
    private sealed class Navegacion : INavigationService
    {
        public Task MostrarRutaAsync() => Task.CompletedTask;
        public Task MostrarLoginAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task AutoSyncRespetaPantallaFormularioYEnvioEnCurso()
    {
        await Login();
        var envio = service.ObtenerRuta().Envios[0];
        var vm = new RutaViewModel(service, new Navegacion(), NullLogger<RutaViewModel>.Instance);
        service.RegistrarEntrega(envio.Id, "Receptor", "");
        var requests = handler.Requests.Count;
        await vm.AutoSincronizarAsync();
        Assert.Equal(requests, handler.Requests.Count);
        vm.PantallaActiva = true;
        vm.EnvioSeleccionado = service.ObtenerRuta().Envios[1];
        await vm.AutoSincronizarAsync();
        Assert.Equal(requests, handler.Requests.Count);
        vm.EnvioSeleccionado = null;
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Espera = liberar.Task;
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta([new(envio.Id, EstadoEnvio.Entregado, 0)], [])));
        var automatico = vm.AutoSincronizarAsync();
        Assert.True(vm.Ocupado);
        await vm.SincronizarCommand.ExecuteAsync(null);
        vm.SeleccionarEntregaCommand.Execute(service.ObtenerRuta().Envios[1]);
        Assert.Null(vm.EnvioSeleccionado);
        liberar.SetResult();
        await automatico;
        Assert.False(vm.Ocupado);
        Assert.Equal(requests + 1, handler.Requests.Count);
        Assert.Equal(EstadoSincronizacion.Sincronizado, service.ObtenerRuta().Envios[0].Sincronizacion);
    }

    [Fact]
    public async Task LoteLimitadoA100NoPierdeElResto()
    {
        await Login();
        var estado = data.Leer();
        var template = estado.Ruta!.Envios[0];
        var envios = Enumerable.Range(0, 101).Select(i =>
        {
            var id = Guid.NewGuid();
            return template with
            {
                Id = id, NumeroSeguimiento = $"TEST-{i}", Sincronizacion = EstadoSincronizacion.Pendiente,
                Entrega = new(Guid.NewGuid(), id, "Receptor", "", DateTimeOffset.Now)
            };
        }).ToArray();
        data.Guardar(estado with { Ruta = estado.Ruta with { Envios = envios },
            EstadosServidor = envios.ToDictionary(e => e.Id, e => e.Estado) });
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta(envios.Take(100)
            .Select(e => new AplicadoContrato(e.Id, EstadoEnvio.Entregado, 0)).ToArray(), [])));
        Assert.Equal(new ResultadoSincronizacion(100, 0, 1), await service.SincronizarAsync());
        Assert.Equal(100, ContratosRepartidor.Leer<SincronizacionSolicitud>(handler.Requests.Last().Body).Eventos.Count);
        Assert.Equal(envios[^1].Entrega, service.ObtenerRuta().Envios[^1].Entrega);
        Assert.Equal(EstadoSincronizacion.Pendiente, service.ObtenerRuta().Envios[^1].Sincronizacion);
    }

    [Fact]
    public async Task IntentoSinMotivoYCoordenadasIncompletasNoMutanCola()
    {
        await Login();
        var envio = service.ObtenerRuta().Envios[0];
        var intento = new EntregaPendiente(Guid.NewGuid(), envio.Id, "", "", DateTimeOffset.Now, EstadoEnvio.NoEntregado);
        Assert.Throws<InvalidOperationException>(() => service.RegistrarEvento(intento));
        Assert.Throws<InvalidOperationException>(() => service.RegistrarEvento(intento with { Observaciones = "Ausente", Latitud = 1 }));
        Assert.Null(service.ObtenerRuta().Envios[0].Entrega);
    }

    [Fact]
    public async Task CargaConfirmadaAntesDeEntregaYConservaAmbosComprobantes()
    {
        await Login();
        var estado = data.Leer();
        var envio = estado.Ruta!.Envios[0];
        data.Guardar(estado with { Ruta = estado.Ruta with
        {
            Envios = estado.Ruta.Envios.Select(e => e.Id == envio.Id ? e with { Estado = EstadoEnvio.AsignadoARuta } : e).ToArray()
        } });
        var carga = new EntregaPendiente(Guid.NewGuid(), envio.Id, "", "", DateTimeOffset.Now,
            EstadoEnvio.EnTransito, CodigoEscaneado: envio.NumeroSeguimiento);
        service.RegistrarEvento(carga);
        Assert.Throws<InvalidOperationException>(() => service.RegistrarEntrega(envio.Id, "Receptor", ""));
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta([new(envio.Id, EstadoEnvio.EnTransito, 0)], [])));
        await service.SincronizarAsync();
        Assert.Equal(carga, service.ObtenerRuta().Envios[0].Historial![0]);
        var intento = new EntregaPendiente(Guid.NewGuid(), envio.Id, "", "Ausente", DateTimeOffset.Now,
            EstadoEnvio.NoEntregado, Convert.ToBase64String([255, 216, 255, 217]), "[[{\"x\":1,\"y\":2}]]", -34.6, -58.4);
        service.RegistrarEvento(intento);
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta([new(envio.Id, EstadoEnvio.NoEntregado, 1)], [])));
        await service.SincronizarAsync();
        var enviado = ContratosRepartidor.Leer<SincronizacionSolicitud>(handler.Requests.Last().Body).Eventos.Single();
        Assert.Equal(intento.Foto, enviado.Foto);
        Assert.Equal(intento.Firma, enviado.Firma);
        Assert.Equal(intento.Fecha, enviado.FechaDispositivo);
        Assert.Equal(intento.Latitud, enviado.Latitud);
        Assert.Equal(2, service.ObtenerRuta().Envios[0].Historial!.Count);
        handler.Respuestas.Enqueue(Ok(Array.Empty<RutaContrato>()));
        await service.DescargarRutaAsync();
        Assert.Empty(service.ObtenerRuta().Envios);
        Assert.Equal(2, data.Leer().ComprobantesConfirmados!.Count);
        Assert.Equal(carga.Id, data.Leer().ComprobantesConfirmados![0].Id);
        Assert.Equal(intento.Id, data.Leer().ComprobantesConfirmados![1].Id);
    }

    [Fact]
    public async Task OperacionesConcurrentesNoPuedenSobrescribirEventosNiCola()
    {
        await Login();
        var envio = service.ObtenerRuta().Envios[0];
        service.RegistrarEntrega(envio.Id, "Receptor", "");
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Espera = liberar.Task;
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta([new(envio.Id, EstadoEnvio.Entregado, 0)], [])));
        var sincronizando = service.SincronizarAsync();
        try
        {
            Assert.Throws<InvalidOperationException>(() => service.RegistrarEntrega(service.ObtenerRuta().Envios[1].Id, "Otro", ""));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SincronizarAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DescargarRutaAsync());
            Assert.Throws<InvalidOperationException>(() => service.CerrarSesion());
        }
        finally { liberar.SetResult(); }
        await sincronizando;
        Assert.Equal(EstadoSincronizacion.Sincronizado, service.ObtenerRuta().Envios[0].Sincronizacion);
    }

    [Fact]
    public async Task RechazadosNoSeReenvianAutomaticamente()
    {
        await RechazoConservaComprobanteYEstadoServidor();
        var requests = handler.Requests.Count;
        Assert.Equal(new ResultadoSincronizacion(0, 0, 1), await service.SincronizarAsync());
        Assert.Equal(requests, handler.Requests.Count);
        var previo = service.ObtenerRuta().Envios[0].Entrega;
        service.ReintentarRechazados();
        Assert.Equal(previo, service.ObtenerRuta().Envios[0].Entrega);
        handler.Respuestas.Enqueue(Ok(new SincronizacionRespuesta([], [new(previo!.EnvioId, "Aun en conflicto", EstadoEnvio.Extraviado)])));
        await service.SincronizarAsync();
        Assert.Equal(previo.Id, ContratosRepartidor.Leer<SincronizacionSolicitud>(handler.Requests.Last().Body).Eventos.Single().EventoId);
    }

    [Fact]
    public async Task ClienteDispositivosUsaBearerYTokenEscapado()
    {
        await Login();
        handler.Respuestas.Enqueue(new(System.Net.HttpStatusCode.Created));
        await service.RegistrarDispositivoAsync("token:abc", "android");
        Assert.Contains("\"plataforma\":\"android\"", handler.Requests.Last().Body);
        Assert.NotNull(handler.Requests.Last().Authorization);
        handler.Respuestas.Enqueue(new(System.Net.HttpStatusCode.NoContent));
        await service.BorrarDispositivoAsync("token:abc");
        Assert.EndsWith("token%3Aabc", handler.Requests.Last().Path);
    }

    [Fact]
    public async Task MigracionCifradaConservaColaYDetectaClaveIncorrecta()
    {
        await Login();
        service.RegistrarEntrega(service.ObtenerRuta().Envios[0].Id, "Secreto receptor", "Secreto evidencia");
        var anterior = data.Leer().Ruta!.Envios[0].Entrega;
        var path = Path.Combine(directory, "api.db");
        var key = RandomNumberGenerator.GetBytes(32);
        var cifrada = new RepartidorLocalData(path, new SnapshotCipher(() => key));
        Assert.Equal(anterior, cifrada.Leer().Ruta!.Envios[0].Entrega);
        Assert.Equal(anterior, cifrada.Leer().Ruta!.Envios[0].Entrega);
        Assert.DoesNotContain("Secreto", File.ReadAllText(path));
        var incorrecta = new RepartidorLocalData(path, new SnapshotCipher(() => RandomNumberGenerator.GetBytes(32)));
        Assert.Throws<InvalidOperationException>(() => incorrecta.Leer());
        Assert.Equal(anterior, cifrada.Leer().Ruta!.Envios[0].Entrega);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Datos FROM EstadoDemo WHERE Id=1";
        Assert.StartsWith("aesgcm1:", (string)command.ExecuteScalar()!);
    }
}
