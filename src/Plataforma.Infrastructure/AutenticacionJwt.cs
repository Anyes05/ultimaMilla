using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

public static class AutenticacionJwt
{
    public static IServiceCollection AddAutenticacionJwt(
        this IServiceCollection services,
        IConfiguration configuration,
        string? rutaLogin = null,
        string cookieNombre = CookieAcceso.Nombre)
    {
        var clave = Clave(configuration);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opciones =>
            {
                opciones.MapInboundClaims = false;
                opciones.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = Emisor(configuration),
                    ValidateAudience = true,
                    ValidAudience = Audiencia(configuration),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave)),
                    ValidateLifetime = true,
                    RoleClaimType = ClaimsPlataforma.Rol,
                    NameClaimType = "unique_name"
                };

                opciones.Events = new JwtBearerEvents
                {
                    OnMessageReceived = contexto =>
                    {
                        if (string.IsNullOrEmpty(contexto.Token) &&
                            contexto.Request.Cookies.TryGetValue(cookieNombre, out var cookie))
                        {
                            contexto.Token = cookie;
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = contexto =>
                    {
                        if (string.IsNullOrEmpty(rutaLogin) || contexto.Request.Path.StartsWithSegments("/api") || contexto.Request.Path.StartsWithSegments("/tecnico"))
                            return Task.CompletedTask;

                        contexto.HandleResponse();
                        contexto.Response.Redirect(rutaLogin);
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static string Clave(IConfiguration configuration) =>
        configuration["Jwt:Clave"]
        ?? throw new InvalidOperationException("Defina Jwt:Clave para firmar los tokens.");

    public static string Emisor(IConfiguration configuration) =>
        configuration["Jwt:Emisor"] ?? "ultima-milla";

    public static string Audiencia(IConfiguration configuration) =>
        configuration["Jwt:Audiencia"] ?? "ultima-milla";

    /// <summary>Vigencia del JWT en horas (por defecto 8; antes era 12 fijas).</summary>
    public static int HorasVigencia(IConfiguration configuration)
    {
        var valor = configuration["Jwt:HorasVigencia"];
        if (int.TryParse(valor, out var horas) && horas is >= 1 and <= 24)
            return horas;
        return 8;
    }
}

public static class CookieAcceso
{
    public const string Nombre = "acceso";
    public const string Portal = "acceso_portal";
    public const string Backoffice = "acceso_backoffice";

    public static CookieOptions Opciones => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true
    };
}
