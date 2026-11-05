using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Plataforma.Mobile.Data;
using Plataforma.Mobile.Models;

namespace Plataforma.Mobile.Services;

public interface ITokenRepartidor
{
    Task<string?> LeerAsync();
    Task GuardarAsync(string token);
    void Borrar();
}

public sealed class RepartidorHttpService(
    HttpClient http, RepartidorLocalData data, ITokenRepartidor tokens) : IRepartidorService
{
    private readonly RepartidorDemoService local = new(data);
    private readonly SemaphoreSlim operaciones = new(1, 1);
    private Uri? endpoint;
    public void ConfigurarEndpoint(string url)
    {
        using var operacion = ExigirOperacion();
        if (data.Leer().Ruta is not null)
            throw new InvalidOperationException("Cierra la sesion sin pendientes antes de cambiar de servidor.");
        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "https" || uri.Scheme == "http" &&
                (uri.IsLoopback || uri.Host == "10.0.2.2")) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Usa HTTPS; HTTP solo para localhost o emulador 10.0.2.2.");
        endpoint = uri;
    }
    private IDisposable ExigirOperacion()
    {
        if (!operaciones.Wait(0))
            throw new InvalidOperationException("Hay una operacion en curso. Espera antes de modificar la cola.");
        return new Liberar(operaciones);
    }
    private sealed class Liberar(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
    public void RegistrarEvento(EntregaPendiente evento)
    {
        using var operacion = ExigirOperacion();
        local.RegistrarEvento(evento);
    }
    public void ReintentarRechazados()
    {
        using var operacion = ExigirOperacion();
        local.ReintentarRechazados();
    }
    public bool ModoDemo { get => false; set { if (value) throw new InvalidOperationException("Selecciona el servicio demo."); } }
    public bool HaySesion => local.HaySesion;
    public bool ConexionSimulada { get; set; } = true;
    public bool IniciarSesion(string usuario, string clave) =>
        throw new InvalidOperationException("El acceso HTTP requiere IniciarSesionAsync.");
    public ResultadoSincronizacion Sincronizar() =>
        throw new InvalidOperationException("La sincronizacion HTTP requiere SincronizarAsync.");
    public Ruta ObtenerRuta() => local.ObtenerRuta();
    public void RegistrarEntrega(Guid envioId, string nombreReceptor, string observaciones)
    {
        using var operacion = ExigirOperacion();
        local.RegistrarEntrega(envioId, nombreReceptor, observaciones);
    }

    public async Task DescargarRutaAsync()
    {
        using var operacion = ExigirOperacion();
        ExigirConexion();
        _ = ObtenerRuta();
        var estado = data.Leer();
        if (estado.Ruta!.Envios.Any(envio =>
            envio.Sincronizacion is EstadoSincronizacion.Pendiente or EstadoSincronizacion.Rechazado))
            throw new InvalidOperationException("Resuelve las entregas pendientes o rechazadas antes de reemplazar la ruta. No se borraron datos.");
        var token = await tokens.LeerAsync();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Inicia sesion para actualizar tu ruta. Tus comprobantes siguen guardados.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/repartidor/rutas/hoy");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await EnviarAsync(request);
        ExigirExito(response);
        var ruta = ConvertirRutas(ContratosRepartidor.Leer<RutaContrato[]>(await response.Content.ReadAsStringAsync()));
        ruta = ruta with
        {
            Envios = ruta.Envios.Select(e => e with
            {
                Historial = estado.ComprobantesConfirmados?.Where(c => c.EnvioId == e.Id).ToArray()
                    ?? estado.Ruta.Envios.SingleOrDefault(previo => previo.Id == e.Id)?.Historial
            }).ToArray()
        };
        data.Guardar(estado with
        {
            Ruta = ruta,
            EstadosServidor = ruta.Envios.ToDictionary(envio => envio.Id, envio => envio.Estado)
        });
    }

    public async Task<bool> IniciarSesionAsync(string usuario, string clave)
    {
        using var operacion = ExigirOperacion();
        ExigirConexion();
        using var login = new HttpRequestMessage(HttpMethod.Post, "api/repartidor/login")
        {
            Content = Json(new LoginSolicitud(usuario, clave))
        };
        using var response = await EnviarAsync(login);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return false;
        ExigirExito(response);
        var sesion = ContratosRepartidor.Leer<LoginRespuesta>(await response.Content.ReadAsStringAsync());
        if (sesion.Rol != "repartidor" || string.IsNullOrWhiteSpace(sesion.Token)
            || sesion.RepartidorId == Guid.Empty || sesion.OperadorId == Guid.Empty)
            throw new InvalidOperationException("No pudimos verificar tu cuenta. Intenta ingresar otra vez. Si continua, contacta al operador.");

        var estado = data.Leer();
        if (estado.Ruta is not null &&
            (estado.RepartidorId != sesion.RepartidorId || estado.OperadorId != sesion.OperadorId))
            throw new InvalidOperationException("Hay datos de otro repartidor. No se reemplazaron; cierra su sesion primero.");
        // Una nueva autenticacion no puede reemplazar comprobantes locales.
        if (estado.Ruta is null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/repartidor/rutas/hoy");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sesion.Token);
            using var rutasResponse = await EnviarAsync(request);
            ExigirExito(rutasResponse);
            var rutas = ContratosRepartidor.Leer<RutaContrato[]>(await rutasResponse.Content.ReadAsStringAsync());
            var ruta = ConvertirRutas(rutas);
            estado = new EstadoLocalDemo(ruta, true,
                ruta.Envios.ToDictionary(envio => envio.Id, envio => envio.Estado),
                sesion.RepartidorId, sesion.OperadorId);
        }
        await tokens.GuardarAsync(sesion.Token);
        data.Guardar(estado with { HaySesion = true });
        return true;
    }

    public async Task<ResultadoSincronizacion> SincronizarAsync()
    {
        using var operacion = ExigirOperacion();
        ExigirConexion();
        var ruta = ObtenerRuta();
        var token = await tokens.LeerAsync();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Inicia sesion para sincronizar. Tus comprobantes siguen guardados.");
        var pendientes = ruta.Envios.Where(envio =>
            envio.Sincronizacion == EstadoSincronizacion.Pendiente).Take(100).ToArray();
        if (pendientes.Length == 0)
            return new ResultadoSincronizacion(0, 0, ruta.Envios.Count(e => e.Sincronizacion == EstadoSincronizacion.Rechazado));
        var eventos = pendientes.Select(envio =>
        {
            var entrega = envio.Entrega ?? throw new InvalidOperationException("Hay un comprobante incompleto. No borres los datos; solicita ayuda al operador.");
            return new EntregaContrato(entrega.Id, envio.Id, entrega.Estado,
                entrega.NombreReceptor, entrega.Observaciones, entrega.Fecha.ToUniversalTime(),
                entrega.Foto, entrega.Firma, entrega.Latitud, entrega.Longitud, entrega.CodigoEscaneado);
        }).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/repartidor/sincronizacion")
        {
            Content = Json(new SincronizacionSolicitud(ruta.Fecha, eventos))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await EnviarAsync(request);
        ExigirExito(response);
        var resultado = ContratosRepartidor.Leer<SincronizacionRespuesta>(await response.Content.ReadAsStringAsync());
        if (resultado.Aplicados is null || resultado.Rechazados is null ||
            resultado.Aplicados.Any(item => item is null) || resultado.Rechazados.Any(item => item is null))
            throw new InvalidOperationException("No pudimos confirmar todos los registros. Tus comprobantes siguen guardados; vuelve a sincronizar.");
        var ids = resultado.Aplicados.Select(item => item.EnvioId)
            .Concat(resultado.Rechazados.Select(item => item.EnvioId)).ToArray();
        if (ids.Distinct().Count() != ids.Length ||
            !ids.ToHashSet().SetEquals(pendientes.Select(envio => envio.Id)) ||
            resultado.Aplicados.Any(item => eventos.SingleOrDefault(e => e.EnvioId == item.EnvioId)?.Estado != item.Estado) ||
            resultado.Rechazados.Any(item => string.IsNullOrWhiteSpace(item.Motivo)))
            throw new InvalidOperationException("La confirmacion recibida no coincide con tus registros. No se marcaron como sincronizados; contacta al operador si vuelve a ocurrir.");
        var estado = data.Leer();
        var envios = estado.Ruta!.Envios.Select(envio =>
        {
            var aplicado = resultado.Aplicados.SingleOrDefault(item => item.EnvioId == envio.Id);
            if (aplicado is not null)
                return envio with
                {
                    Estado = aplicado.Estado, Sincronizacion = EstadoSincronizacion.Sincronizado, MotivoRechazo = null,
                    Historial = (envio.Historial ?? []).Concat(envio.Entrega is null ? [] : new[] { envio.Entrega }).ToArray(),
                    Entrega = aplicado.Estado == EstadoEnvio.EnTransito ? null : envio.Entrega
                };
            var rechazo = resultado.Rechazados.SingleOrDefault(item => item.EnvioId == envio.Id);
            return rechazo is null ? envio : envio with
            {
                Estado = rechazo.EstadoActual ?? envio.Estado,
                Sincronizacion = EstadoSincronizacion.Rechazado,
                MotivoRechazo = rechazo.Motivo
            };
        }).ToArray();
        var confirmados = (estado.ComprobantesConfirmados ?? [])
            .Concat(pendientes.Where(e => resultado.Aplicados.Any(a => a.EnvioId == e.Id)).Select(e => e.Entrega!))
            .DistinctBy(e => e.Id).ToArray();
        data.Guardar(estado with { Ruta = estado.Ruta with { Envios = envios }, ComprobantesConfirmados = confirmados });
        return new ResultadoSincronizacion(resultado.Aplicados.Count, resultado.Rechazados.Count,
            envios.Count(envio => envio.Sincronizacion is EstadoSincronizacion.Pendiente or EstadoSincronizacion.Rechazado));
    }

    public void CerrarSesion()
    {
        using var operacion = ExigirOperacion();
        local.CerrarSesion();
        tokens.Borrar();
        ConexionSimulada = true;
    }

    public Task RegistrarDispositivoAsync(string token, string plataforma)
    {
        if (plataforma is not ("android" or "ios" or "web"))
            throw new InvalidOperationException("No se reconoce el tipo de dispositivo. Solicita ayuda al operador.");
        return DispositivoAsync(HttpMethod.Post, token, plataforma);
    }

    public Task BorrarDispositivoAsync(string token) => DispositivoAsync(HttpMethod.Delete, token, null);

    private async Task DispositivoAsync(HttpMethod method, string token, string? plataforma)
    {
        using var operacion = ExigirOperacion();
        ExigirConexion();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Escribe el codigo del dispositivo antes de registrarlo o eliminarlo.");
        var jwt = await tokens.LeerAsync();
        if (string.IsNullOrWhiteSpace(jwt))
            throw new InvalidOperationException("Inicia sesion para gestionar este dispositivo.");
        using var request = new HttpRequestMessage(method, method == HttpMethod.Post
            ? "api/repartidor/dispositivos" : $"api/repartidor/dispositivos/{Uri.EscapeDataString(token)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        if (method == HttpMethod.Post) request.Content = Json(new { token, plataforma });
        using var response = await EnviarAsync(request);
        if (method == HttpMethod.Delete && response.StatusCode == HttpStatusCode.NotFound) return;
        ExigirExito(response);
    }

    private static Ruta ConvertirRutas(RutaContrato[] rutas)
    {
        if (rutas.Length == 0)
            return new Ruta(Guid.Empty, DateOnly.FromDateTime(DateTime.UtcNow), "Sin rutas asignadas", []);
        if (rutas.Any(ruta => ruta is null || ruta.Id == Guid.Empty || ruta.Fecha == default ||
            ruta.Operador is null || string.IsNullOrWhiteSpace(ruta.Operador.Nombre) || ruta.Envios is null))
            throw new InvalidOperationException("La ruta recibida esta incompleta. Intenta actualizarla de nuevo; si continua, avisa al operador.");
        if (rutas.Any(ruta => ruta.Fecha != rutas[0].Fecha || ruta.Operador != rutas[0].Operador))
            throw new InvalidOperationException("Las rutas recibidas no corresponden a una misma jornada. Solicita al operador que revise tu asignacion.");
        var envios = rutas.SelectMany(ruta => ruta.Envios).ToArray();
        if (envios.Any(envio => envio is null || envio.Id == Guid.Empty) ||
            envios.Select(envio => envio.Id).Distinct().Count() != envios.Length)
            throw new InvalidOperationException("La ruta contiene envios incompletos o repetidos. Solicita al operador que revise tu asignacion.");
        return new Ruta(rutas[0].Id, rutas[0].Fecha, rutas[0].Operador.Nombre,
            envios.Select(envio => new Envio(envio.Id, envio.NumeroSeguimiento, envio.Destinatario,
                envio.DireccionEntrega, envio.Estado, null, EstadoSincronizacion.SinCambios, null)).ToArray());
    }

    private void ExigirConexion()
    {
        if (!ConexionSimulada)
            throw new InvalidOperationException("La conexion esta desactivada. Tus comprobantes siguen guardados.");
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage request)
    {
        if (endpoint is not null && request.RequestUri is { IsAbsoluteUri: false } relativa)
            request.RequestUri = new Uri(endpoint, relativa);
        try { return await http.SendAsync(request); }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("No pudimos conectar. Comprueba Internet e intentalo de nuevo. Tus comprobantes siguen guardados.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new InvalidOperationException("La respuesta esta tardando demasiado. Intentalo de nuevo en unos momentos; tus comprobantes siguen guardados.", ex);
        }
    }

    private static StringContent Json<T>(T value) =>
        new(ContratosRepartidor.Escribir(value), Encoding.UTF8, "application/json");

    private static void ExigirExito(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Tu sesion vencio y necesitas identificarte de nuevo. Si no puedes volver al acceso, solicita ayuda al operador. No borres los datos: tus comprobantes siguen guardados.",
                HttpStatusCode.Forbidden => "Tu cuenta no tiene permiso para esta accion. Contacta al operador; tus comprobantes siguen guardados.",
                HttpStatusCode.BadRequest => "No pudimos aceptar los datos enviados. Revisa la informacion y, si vuelve a ocurrir, contacta al operador. Tus comprobantes siguen guardados.",
                HttpStatusCode.NotFound => "No encontramos la informacion solicitada. Intenta actualizar tu ruta o consulta al operador.",
                HttpStatusCode.Conflict => "El registro cambio mientras trabajabas. Actualiza la informacion cuando no tengas pendientes o pide ayuda al operador. Tus comprobantes siguen guardados.",
                HttpStatusCode.RequestEntityTooLarge => "Los comprobantes enviados ocupan demasiado espacio. No se confirmo la operacion; tus datos siguen guardados. Solicita ayuda al operador.",
                HttpStatusCode.TooManyRequests => "Hay demasiadas solicitudes en este momento. Espera un poco y vuelve a intentar. Tus comprobantes siguen guardados.",
                _ => "No pudimos confirmar la operacion por un problema del servicio. Intentalo mas tarde; tus comprobantes siguen guardados."
            }, new HttpRequestException($"Respuesta HTTP {(int)response.StatusCode}.", null, response.StatusCode));
    }
}
