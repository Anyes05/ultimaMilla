using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Plataforma.Infrastructure;

/// <summary>
/// Seguridad web común de Backoffice y Portal (letra 6.3: "CORS restringido… cabeceras seguras").
/// <list type="bullet">
/// <item><b>IP del cliente:</b> X-Forwarded-For se acepta solo si lo agrega un proxy de la red interna (ALB, NGINX,
/// la red de compose). Lo que el cliente escriba en ese encabezado no cambia su IP, y el límite de tasa no se salta.</item>
/// <item><b>CORS:</b> ningún origen externo, salvo los listados en <c>Cors:OrigenesPermitidos</c>. Las pantallas son
/// del mismo origen y la app móvil no es un navegador, así que por defecto no hace falta abrir nada.</item>
/// <item><b>Cabeceras:</b> CSP, no ser embebida en otro sitio, sin adivinar tipos, y referer acotado.</item>
/// </list>
/// El borde (TLS, HSTS del ALB, CORS del Ingress) es de infraestructura.
/// </summary>
public static class SeguridadWeb
{
    public const string PoliticaCors = "plataforma";

    // Redes privadas: pods y nodos de EKS (VPC 172.31.0.0/16), compose y loopback.
    private static readonly string[] RedesConfiables = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8"];

    public static IServiceCollection AddSeguridadWeb(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(opciones =>
        {
            opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Se recorre la cadena de derecha a izquierda mientras el salto sea de la red interna;
            // el primer salto externo es el cliente. Lo que esté más a la izquierda (escrito por el cliente) se ignora.
            opciones.ForwardLimit = null;
            opciones.KnownProxies.Clear();
            opciones.KnownIPNetworks.Clear();
            foreach (var red in RedesConfiables)
                opciones.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(red));
        });

        var origenes = configuration.GetSection("Cors:OrigenesPermitidos").Get<string[]>() ?? [];
        services.AddCors(opciones => opciones.AddPolicy(PoliticaCors, politica =>
        {
            if (origenes.Length > 0)
                politica.WithOrigins(origenes).AllowAnyHeader().WithMethods("GET", "POST", "PUT", "DELETE");
        }));
        return services;
    }

    /// <summary>Va primero en el pipeline, antes del log por request y del límite de tasa.</summary>
    public static WebApplication UseSeguridadWeb(this WebApplication app)
    {
        app.UseForwardedHeaders();

        app.Use(async (contexto, siguiente) =>
        {
            var cabeceras = contexto.Response.Headers;
            var host = contexto.Request.Host.Value;
            // 'unsafe-inline' porque las páginas usan scripts y estilos en línea (Razor, Blazor y su importmap);
            // el resto queda acotado al propio sitio. connect-src incluye el WebSocket de SignalR del mismo host.
            cabeceras.ContentSecurityPolicy =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; font-src 'self' data:; " +
                $"connect-src 'self' wss://{host} ws://{host}; " +
                "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
            cabeceras.XContentTypeOptions = "nosniff";
            cabeceras.XFrameOptions = "DENY";
            cabeceras["Referrer-Policy"] = "strict-origin-when-cross-origin";
            cabeceras["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            await siguiente();
        });

        app.UseCors(PoliticaCors);
        return app;
    }

    /// <summary>Sin el encabezado "Server: Kestrel": no anuncia la tecnología del servidor.</summary>
    public static WebApplicationBuilder SinCabeceraServidor(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(opciones => opciones.AddServerHeader = false);
        return builder;
    }
}
