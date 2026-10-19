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
    public bool ModoDemo { get => false; set { if (value) throw new InvalidOperationException("Selecciona el servicio demo."); } }
    public bool HaySesion => local.HaySesion;
    public bool ConexionSimulada { get; set; } = true;
    public bool IniciarSesion(string usuario, string clave) =>
        throw new InvalidOperationException("El acceso HTTP requiere IniciarSesionAsync.");
    public ResultadoSincronizacion Sincronizar() =>
        throw new InvalidOperationException("La sincronizacion HTTP requiere SincronizarAsync.");
    public Ruta ObtenerRuta() => local.ObtenerRuta();
    public void RegistrarEntrega(Guid envioId, string nombreReceptor, string observaciones) =>
        local.RegistrarEntrega(envioId, nombreReceptor, observaciones);

    public async Task DescargarRutaAsync()
    {
        ExigirConexion();
        _ = ObtenerRuta();
        var estado = data.Leer();
        if (estado.Ruta!.Envios.Any(envio =>
            envio.Sincronizacion is EstadoSincronizacion.Pendiente or EstadoSincronizacion.Rechazado))
            throw new InvalidOperationException("Resuelve las entregas pendientes o rechazadas antes de reemplazar la ruta. No se borraron datos.");
        var token = await tokens.LeerAsync();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Inicia sesion con la API para descargar la ruta.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/repartidor/rutas/hoy");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await EnviarAsync(request);
        ExigirExito(response);
        var ruta = ConvertirRutas(ContratosRepartidor.Leer<RutaContrato[]>(await response.Content.ReadAsStringAsync()));
        data.Guardar(estado with
        {
            Ruta = ruta,
            EstadosServidor = ruta.Envios.ToDictionary(envio => envio.Id, envio => envio.Estado)
        });
    }

    public async Task<bool> IniciarSesionAsync(string usuario, string clave)
    {
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
            throw new InvalidOperationException("La API no devolvio una sesion de repartidor valida.");

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
        ExigirConexion();
        var ruta = ObtenerRuta();
        var token = await tokens.LeerAsync();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Inicia sesion con la API para sincronizar. La cola sigue guardada.");
        var pendientes = ruta.Envios.Where(envio =>
            envio.Sincronizacion is EstadoSincronizacion.Pendiente or EstadoSincronizacion.Rechazado).ToArray();
        if (pendientes.Length == 0)
            return new ResultadoSincronizacion(0, 0, 0);
        var eventos = pendientes.Select(envio =>
        {
            var entrega = envio.Entrega ?? throw new InvalidOperationException("Comprobante local incompleto.");
            return new EntregaContrato(entrega.Id, envio.Id, EstadoEnvio.Entregado,
                entrega.NombreReceptor, entrega.Observaciones, entrega.Fecha);
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
            throw new InvalidOperationException("Respuesta de sincronizacion incompleta. Cola conservada.");
        var ids = resultado.Aplicados.Select(item => item.EnvioId)
            .Concat(resultado.Rechazados.Select(item => item.EnvioId)).ToArray();
        if (ids.Distinct().Count() != ids.Length ||
            !ids.ToHashSet().SetEquals(pendientes.Select(envio => envio.Id)) ||
            resultado.Aplicados.Any(item => item.Estado != EstadoEnvio.Entregado) ||
            resultado.Rechazados.Any(item => string.IsNullOrWhiteSpace(item.Motivo)))
            throw new InvalidOperationException("Respuesta de sincronizacion inconsistente. Cola conservada.");
        var estado = data.Leer();
        var envios = estado.Ruta!.Envios.Select(envio =>
        {
            var aplicado = resultado.Aplicados.SingleOrDefault(item => item.EnvioId == envio.Id);
            if (aplicado is not null)
                return envio with { Estado = aplicado.Estado, Sincronizacion = EstadoSincronizacion.Sincronizado, MotivoRechazo = null };
            var rechazo = resultado.Rechazados.SingleOrDefault(item => item.EnvioId == envio.Id);
            return rechazo is null ? envio : envio with
            {
                Estado = rechazo.EstadoActual ?? envio.Estado,
                Sincronizacion = EstadoSincronizacion.Rechazado,
                MotivoRechazo = rechazo.Motivo
            };
        }).ToArray();
        data.Guardar(estado with { Ruta = estado.Ruta with { Envios = envios } });
        return new ResultadoSincronizacion(resultado.Aplicados.Count, resultado.Rechazados.Count,
            envios.Count(envio => envio.Sincronizacion is EstadoSincronizacion.Pendiente or EstadoSincronizacion.Rechazado));
    }

    public void CerrarSesion()
    {
        local.CerrarSesion();
        tokens.Borrar();
        ConexionSimulada = true;
    }

    private static Ruta ConvertirRutas(RutaContrato[] rutas)
    {
        if (rutas.Length == 0)
            return new Ruta(Guid.Empty, DateOnly.FromDateTime(DateTime.UtcNow), "Sin rutas asignadas", []);
        if (rutas.Any(ruta => ruta is null || ruta.Id == Guid.Empty || ruta.Fecha == default ||
            ruta.Operador is null || string.IsNullOrWhiteSpace(ruta.Operador.Nombre) || ruta.Envios is null))
            throw new InvalidOperationException("La API devolvio rutas incompletas.");
        if (rutas.Any(ruta => ruta.Fecha != rutas[0].Fecha || ruta.Operador != rutas[0].Operador))
            throw new InvalidOperationException("Las rutas recibidas no corresponden al mismo dia y operador.");
        var envios = rutas.SelectMany(ruta => ruta.Envios).ToArray();
        if (envios.Any(envio => envio is null || envio.Id == Guid.Empty) ||
            envios.Select(envio => envio.Id).Distinct().Count() != envios.Length)
            throw new InvalidOperationException("La API devolvio envios incompletos o duplicados.");
        return new Ruta(rutas[0].Id, rutas[0].Fecha, rutas[0].Operador.Nombre,
            envios.Select(envio => new Envio(envio.Id, envio.NumeroSeguimiento, envio.Destinatario,
                envio.DireccionEntrega, envio.Estado, null, EstadoSincronizacion.SinCambios, null)).ToArray());
    }

    private void ExigirConexion()
    {
        if (!ConexionSimulada)
            throw new InvalidOperationException("Conexion desactivada. Los datos siguen guardados en SQLite.");
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage request)
    {
        try { return await http.SendAsync(request); }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("No se pudo contactar la API. Cola conservada; reintenta o usa el modo demo explicitamente.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new InvalidOperationException("La API excedio el tiempo de espera. Cola conservada.", ex);
        }
    }

    private static StringContent Json<T>(T value) =>
        new(ContratosRepartidor.Escribir(value), Encoding.UTF8, "application/json");

    private static void ExigirExito(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Sesion vencida o invalida. Vuelve al acceso e inicia sesion; la cola se conserva.",
                HttpStatusCode.Forbidden => "La API no permite esta operacion para tu usuario.",
                _ => $"La API respondio HTTP {(int)response.StatusCode}. No se confirmo la operacion; datos locales conservados."
            });
    }
}
