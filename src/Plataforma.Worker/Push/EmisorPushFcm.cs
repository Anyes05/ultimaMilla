using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Plataforma.Worker.Push;

/// <summary>
/// Push real con Firebase Cloud Messaging, API HTTP v1 (opcional 7.3 y letra 6.11: la app recibe la reasignación
/// urgente aunque esté cerrada). Se autentica con una cuenta de servicio de Google: arma un JWT firmado con su clave
/// privada, lo cambia por un token de acceso (se guarda hasta un minuto antes de vencer) y manda el mensaje.
/// La cuenta de servicio llega por configuración (<c>Push:Fcm:CuentaServicioJson</c>, un secreto del clúster) y
/// nunca se escribe en el repositorio ni en el log.
/// </summary>
public sealed class EmisorPushFcm : IEmisorPush
{
    public const string ClienteHttp = "fcm";
    private const string Alcance = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<EmisorPushFcm> _logger;
    private readonly CuentaServicioGoogle _cuenta;
    private readonly string _proyecto;
    private readonly SemaphoreSlim _renovando = new(1, 1);
    private string? _tokenAcceso;
    private DateTimeOffset _venceToken;

    public EmisorPushFcm(IHttpClientFactory http, ILogger<EmisorPushFcm> logger, CuentaServicioGoogle cuenta, string? proyecto = null)
    {
        _http = http;
        _logger = logger;
        _cuenta = cuenta;
        _proyecto = string.IsNullOrWhiteSpace(proyecto) ? cuenta.ProjectId : proyecto;
    }

    public async Task<ResultadoPush> EnviarAsync(MensajePush mensaje, CancellationToken cancellationToken)
    {
        try
        {
            var token = await ObtenerTokenAsync(cancellationToken);
            using var solicitud = new HttpRequestMessage(
                HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(_proyecto)}/messages:send")
            {
                Content = new StringContent(Cuerpo(mensaje), Encoding.UTF8, "application/json")
            };
            solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var respuesta = await _http.CreateClient(ClienteHttp).SendAsync(solicitud, cancellationToken);
            var texto = respuesta.IsSuccessStatusCode ? string.Empty : await respuesta.Content.ReadAsStringAsync(cancellationToken);
            var resultado = Clasificar(respuesta.StatusCode, texto);
            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                _tokenAcceso = null; // Se pide otro en el próximo intento.
            if (resultado != ResultadoPush.Enviado)
                _logger.LogWarning("FCM respondió {Codigo} ({Resultado}) para ...{Token}",
                    (int)respuesta.StatusCode, resultado, mensaje.Token[^Math.Min(6, mensaje.Token.Length)..]);
            return resultado;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            _logger.LogWarning(ex, "FCM no respondió; se reintenta con espera.");
            return ResultadoPush.ErrorTransitorio;
        }
    }

    /// <summary>
    /// Mensaje de alta prioridad, con notificación visible (Android la muestra con la app cerrada) y los datos
    /// para que la app abra la ruta al tocarla.
    /// </summary>
    internal static string Cuerpo(MensajePush mensaje) => JsonSerializer.Serialize(new
    {
        message = new
        {
            token = mensaje.Token,
            notification = new { title = mensaje.Titulo, body = mensaje.Cuerpo },
            data = mensaje.Datos,
            android = new { priority = "high", notification = new { channel_id = "reasignaciones" } }
        }
    });

    /// <summary>
    /// UNREGISTERED (404) o un token mal formado (400 INVALID_ARGUMENT sobre el token): el dispositivo ya no sirve.
    /// 401/403, 429, 5xx: transitorio, se reintenta con la espera del worker.
    /// </summary>
    internal static ResultadoPush Clasificar(HttpStatusCode codigo, string cuerpo)
    {
        if ((int)codigo is >= 200 and < 300)
            return ResultadoPush.Enviado;
        if (codigo == HttpStatusCode.NotFound || cuerpo.Contains("UNREGISTERED", StringComparison.Ordinal))
            return ResultadoPush.TokenInvalido;
        if (codigo == HttpStatusCode.BadRequest && cuerpo.Contains("INVALID_ARGUMENT", StringComparison.Ordinal)
            && cuerpo.Contains("token", StringComparison.OrdinalIgnoreCase))
            return ResultadoPush.TokenInvalido;
        return ResultadoPush.ErrorTransitorio;
    }

    private async Task<string> ObtenerTokenAsync(CancellationToken cancellationToken)
    {
        if (_tokenAcceso is { } vigente && DateTimeOffset.UtcNow < _venceToken)
            return vigente;

        await _renovando.WaitAsync(cancellationToken);
        try
        {
            if (_tokenAcceso is { } otro && DateTimeOffset.UtcNow < _venceToken)
                return otro;

            var ahora = DateTimeOffset.UtcNow;
            var afirmacion = JwtCuentaServicio(_cuenta, Alcance, ahora);
            using var pedido = new HttpRequestMessage(HttpMethod.Post, _cuenta.TokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = afirmacion
                })
            };
            using var respuesta = await _http.CreateClient(ClienteHttp).SendAsync(pedido, cancellationToken);
            respuesta.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(cancellationToken));
            _tokenAcceso = json.RootElement.GetProperty("access_token").GetString()
                ?? throw new HttpRequestException("Google no devolvió el token de acceso.");
            var segundos = json.RootElement.TryGetProperty("expires_in", out var vence) ? vence.GetInt32() : 3600;
            _venceToken = ahora.AddSeconds(Math.Max(60, segundos - 60));
            return _tokenAcceso;
        }
        finally
        {
            _renovando.Release();
        }
    }

    /// <summary>JWT RS256 de la cuenta de servicio (flujo OAuth 2.0 "JWT bearer" de Google).</summary>
    internal static string JwtCuentaServicio(CuentaServicioGoogle cuenta, string alcance, DateTimeOffset ahora)
    {
        static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var encabezado = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = cuenta.PrivateKeyId }));
        var datos = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = cuenta.ClientEmail,
            scope = alcance,
            aud = cuenta.TokenUri,
            iat = ahora.ToUnixTimeSeconds(),
            exp = ahora.AddMinutes(55).ToUnixTimeSeconds()
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(cuenta.PrivateKey);
        var firma = rsa.SignData(Encoding.ASCII.GetBytes($"{encabezado}.{datos}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{encabezado}.{datos}.{Base64Url(firma)}";
    }
}

/// <summary>Los campos que se usan del JSON de la cuenta de servicio que descarga la consola de Firebase.</summary>
public sealed record CuentaServicioGoogle(string ProjectId, string ClientEmail, string PrivateKey, string PrivateKeyId, string TokenUri)
{
    public static CuentaServicioGoogle Leer(string json)
    {
        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement;
        string Campo(string nombre) =>
            raiz.TryGetProperty(nombre, out var valor) && valor.GetString() is { Length: > 0 } texto
                ? texto
                : throw new InvalidOperationException($"La cuenta de servicio de Firebase no tiene '{nombre}'.");
        return new CuentaServicioGoogle(
            Campo("project_id"),
            Campo("client_email"),
            Campo("private_key"),
            raiz.TryGetProperty("private_key_id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            raiz.TryGetProperty("token_uri", out var uri) && uri.GetString() is { Length: > 0 } tokenUri
                ? tokenUri
                : "https://oauth2.googleapis.com/token");
    }
}
