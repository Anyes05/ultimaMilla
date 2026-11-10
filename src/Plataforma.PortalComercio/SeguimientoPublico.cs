using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Plataforma.Domain;

namespace Plataforma.PortalComercio;

/// <summary>
/// Seguimiento público del destinatario (letra 4, RF 25, 6.2 y 6.3): sin autenticación, con límite de tasa
/// por IP contra la enumeración de envíos, y con el subdominio de tracking apuntando a la página.
/// </summary>
public static class SeguimientoPublico
{
    public const string Ruta = "/seguimiento";

    /// <summary>Consultas por minuto y por IP, sumando todas las réplicas.</summary>
    public const int ConsultasPorMinuto = 30;

    /// <summary>Límite de tasa compartido entre réplicas (Redis, con respaldo local).</summary>
    public static IServiceCollection AddSeguimientoPublico(this IServiceCollection services) =>
        services.AddSingleton(proveedor => new LimitadorSeguimiento(
            proveedor.GetService<StackExchange.Redis.IConnectionMultiplexer>(),
            proveedor.GetRequiredService<ILogger<LimitadorSeguimiento>>(),
            ConsultasPorMinuto));

    /// <summary>
    /// En el host de tracking (configurable en Seguimiento:Host) la raíz abre el seguimiento, así el enlace
    /// tracking.dominio/ funciona cuando el Ingress apunte ese host al Portal.
    /// Toda ruta /seguimiento lleva límite de tasa (compartido entre réplicas), venga por el host que venga.
    /// </summary>
    public static WebApplication UseSeguimientoPublico(this WebApplication app)
    {
        var hostTracking = app.Configuration["Seguimiento:Host"] ?? "tracking.tallernet.cloud-ip.cc";

        app.Use(async (contexto, siguiente) =>
        {
            if (contexto.Request.Path == "/" &&
                string.Equals(contexto.Request.Host.Host, hostTracking, StringComparison.OrdinalIgnoreCase))
                contexto.Request.Path = Ruta;

            await siguiente();
        });

        var limitador = app.Services.GetRequiredService<LimitadorSeguimiento>();
        app.UseWhen(
            contexto => contexto.Request.Path.StartsWithSegments(Ruta),
            seguimiento => seguimiento.Use(async (contexto, siguiente) =>
            {
                if (!await limitador.PermitirAsync(IpCliente(contexto)))
                {
                    contexto.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    contexto.Response.Headers.RetryAfter = ((int)LimitadorSeguimiento.Ventana.TotalSeconds).ToString();
                    contexto.Response.ContentType = "text/plain; charset=utf-8";
                    await contexto.Response.WriteAsync(
                        "Hiciste demasiadas consultas seguidas. Probá de nuevo en un minuto.", contexto.RequestAborted);
                    return;
                }

                await siguiente();
            }));

        return app;
    }

    // La IP ya viene resuelta por UseSeguridadWeb: X-Forwarded-For solo cuenta si lo agregó un proxy interno.
    // Leer el encabezado crudo permitiría saltar el límite escribiendo una IP distinta en cada consulta.
    private static string IpCliente(HttpContext contexto) =>
        contexto.Connection.RemoteIpAddress?.ToString() ?? "sin-ip";

    private const string PropositoRetiro = "Plataforma.SeguimientoRetiro.v1";

    /// <summary>
    /// Enlace público de un retiro (RF 37 a 41, mismo mecanismo que el de los envíos): el operador y el retiro van
    /// cifrados y firmados con las claves compartidas del Portal, así no se exponen identificadores internos ni se
    /// pueden adivinar enlaces de otros retiros.
    /// </summary>
    public static string TokenRetiro(IDataProtectionProvider proveedor, Guid operadorId, Guid retiroId) =>
        proveedor.CreateProtector(PropositoRetiro).Protect($"{operadorId:N}{retiroId:N}");

    public static bool LeerTokenRetiro(IDataProtectionProvider proveedor, string? token, out Guid operadorId, out Guid retiroId)
    {
        operadorId = retiroId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 400)
            return false;

        try
        {
            var texto = proveedor.CreateProtector(PropositoRetiro).Unprotect(token.Trim());
            return texto.Length == 64
                   && Guid.TryParseExact(texto[..32], "N", out operadorId)
                   && Guid.TryParseExact(texto[32..], "N", out retiroId);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Etapas que ve quien sigue un retiro.</summary>
    public static readonly IReadOnlyList<(string Nombre, EstadoRetiro Estado)> EtapasRetiro =
    [
        ("Solicitado", EstadoRetiro.Solicitado),
        ("Programado", EstadoRetiro.Programado),
        ("En camino", EstadoRetiro.EnCurso),
        ("Retirado", EstadoRetiro.Completado)
    ];

    public static string DescripcionRetiro(EstadoRetiro estado) => estado switch
    {
        EstadoRetiro.Solicitado => "Recibimos el pedido de retiro. El operador lo va a asignar a una ruta.",
        EstadoRetiro.Programado => "El retiro ya está asignado a la ruta del día.",
        EstadoRetiro.EnCurso => "El repartidor va en camino a retirar los paquetes.",
        EstadoRetiro.Completado => "Los paquetes fueron retirados.",
        EstadoRetiro.Cancelado => "El retiro fue cancelado.",
        _ => string.Empty
    };

    /// <summary>Color de la marca del operador (RF 5).</summary>
    public static string ColorMarcaCss(string colorMarca) => colorMarca switch
    {
        "azul" => "#0d6efd",
        "verde" => "#198754",
        _ => "#4f46e5"
    };

    /// <summary>Etapas del recorrido normal que ve el destinatario.</summary>
    public static readonly IReadOnlyList<(string Nombre, EstadoEnvio[] Estados)> Etapas =
    [
        ("Recibido", [EstadoEnvio.Admitido]),
        ("En depósito", [EstadoEnvio.EnDeposito]),
        ("En camino", [EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito, EstadoEnvio.EntregadoParcialmente,
            EstadoEnvio.NoEntregado, EstadoEnvio.Reprogramado]),
        ("Entregado", [EstadoEnvio.Entregado])
    ];

    /// <summary>Índice de la etapa alcanzada; -1 si el envío salió del recorrido normal (devolución o extravío).</summary>
    public static int EtapaActual(EstadoEnvio estado)
    {
        for (var i = 0; i < Etapas.Count; i++)
            if (Etapas[i].Estados.Contains(estado))
                return i;
        return -1;
    }

    /// <summary>Explicación para el destinatario, sin datos de terceros ni identificadores internos.</summary>
    public static string Descripcion(EstadoEnvio estado) => estado switch
    {
        EstadoEnvio.Admitido => "El comercio registró tu envío. Pronto llega a nuestro depósito.",
        EstadoEnvio.EnDeposito => "Tu envío está en nuestro depósito, preparándose para salir.",
        EstadoEnvio.AsignadoARuta => "Tu envío ya tiene repartidor asignado y sale en la próxima ruta.",
        EstadoEnvio.EnTransito => "Tu envío está en camino.",
        EstadoEnvio.Entregado => "Tu envío fue entregado.",
        EstadoEnvio.EntregadoParcialmente => "Entregamos parte de tu envío. El resto se entrega en otra visita o vuelve al comercio.",
        EstadoEnvio.NoEntregado => "Intentamos entregarlo y no pudimos. El operador va a definir cómo sigue.",
        EstadoEnvio.Reprogramado => "La entrega se reprogramó para otra visita.",
        EstadoEnvio.EnDevolucion => "No fue posible entregarlo y el envío vuelve al comercio.",
        EstadoEnvio.Devuelto => "El envío fue devuelto al comercio.",
        EstadoEnvio.Extraviado => "Hay un problema con tu envío. Comunicate con el comercio.",
        _ => string.Empty
    };
}
