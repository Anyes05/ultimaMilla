using Plataforma.Infrastructure;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Backoffice;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Plataforma.Avisos;
using Plataforma.Backoffice.Tablero;
using StackExchange.Redis;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAutenticacionJwt(builder.Configuration, "/login", CookieAcceso.Backoffice);
builder.Services.AddRateLimiter(_ => { });

// Claves de Data Protection compartidas entre réplicas (igual que el Portal).
// Sin esto, el token antiforgery emitido por un pod no se puede validar en otro: HTTP 400.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("Plataforma.Backoffice");
var opcionesRedis = CacheRedis.CrearOpciones(builder.Configuration);
if (opcionesRedis is not null)
{
    dataProtection.PersistKeysToStackExchangeRedis(
        ConnectionMultiplexer.Connect(opcionesRedis),
        "plataforma:backoffice:data-protection");
}
// Módulo de avisos (fallidos y reintentos) y tablero en tiempo real alimentado desde la cola.
builder.Services.AddAvisos(builder.Configuration);
builder.Services.AddHostedService<TableroConsumidor>();

builder.Services.ConfigureHttpJsonOptions(opciones =>
{
    foreach (var converter in ObtenerRutasDeHoy.OpcionesJson.Converters)
        opciones.SerializerOptions.Converters.Add(converter);
});
var signalR = builder.Services.AddSignalR();
if (opcionesRedis is not null)
    signalR.AddStackExchangeRedis(opcionesRedis.ToString());

var app = builder.Build();

if (string.Equals(builder.Configuration["Database:MigrateOnStartup"], "true", StringComparison.OrdinalIgnoreCase))
{
    await app.Services.MigrarBaseDeDatosAsync();
    // Esquema "avisos" con su propia tabla de migraciones; lo aplica el mismo job de migración.
    await app.Services.MigrarAvisosAsync();
}

if (string.Equals(builder.Configuration["Database:MigrateOnly"], "true", StringComparison.OrdinalIgnoreCase))
{
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Página que el perfil no puede abrir (URL escrita a mano): en vez de un 403 en blanco, vuelve a Monitoreo con aviso.
// Va antes de la autorización para capturar su 403. Las APIs y el hub siguen respondiendo 403.
app.UseStatusCodePages(contexto =>
{
    var http = contexto.HttpContext;
    if (http.Response.StatusCode == StatusCodes.Status403Forbidden &&
        !http.Request.Path.StartsWithSegments("/api") &&
        !http.Request.Path.StartsWithSegments("/hubs") &&
        !http.Request.Path.StartsWithSegments("/tecnico"))
        http.Response.Redirect("/?sinPermiso=true");
    return Task.CompletedTask;
});

app.Use(async (contexto, siguiente) =>
{
    if (
        string.Equals(contexto.Request.Host.Host, "tracking.tallernet.cloud-ip.cc", StringComparison.OrdinalIgnoreCase) &&
        contexto.Request.Path == "/")
    {
        contexto.Request.Path = "/Seguimiento";
    }

    await siguiente();
});

app.UseRouting();
app.UseWhen(
    contexto => string.Equals(
        contexto.Request.Host.Host,
        "tracking.tallernet.cloud-ip.cc",
        StringComparison.OrdinalIgnoreCase),
    seguimiento => seguimiento.UseRateLimiter(new RateLimiterOptions
    {
        GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
        {
            var ipCliente = contexto.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',', 2)[0].Trim();
            var clave = string.IsNullOrWhiteSpace(ipCliente)
                ? contexto.Connection.RemoteIpAddress?.ToString() ?? "sin-ip"
                : ipCliente;

            return
            RateLimitPartition.GetFixedWindowLimiter(
                clave,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        })
    }));
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/live", () => Results.Ok()).AllowAnonymous();
app.MapGet("/ready", async (PlataformaDbContext context, CancellationToken cancellationToken) =>
    await context.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok()
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

app.MapPost("/session/login", async (HttpRequest request, IniciarSesion iniciarSesion, IEmisorToken emisor, CancellationToken cancellationToken) =>
{
    var formulario = await request.ReadFormAsync(cancellationToken);
    var resultado = await iniciarSesion.Ejecutar(
        formulario["usuario"].ToString(),
        formulario["clave"].ToString(),
        cancellationToken);

    // Solo perfiles del operador: el comercio usa el Portal y el repartidor la app móvil.
    if (resultado is ResultadoInicio.Ok ok && PermisosPerfil.PuedeEntrarAlBackoffice(ok.Sesion.Rol))
    {
        request.HttpContext.Response.Cookies.Append(
            CookieAcceso.Backoffice,
            emisor.Emitir(ok.Sesion),
            CookieAcceso.Opciones);
        return Results.Redirect("/");
    }

    return Results.Redirect("/login?error=1");
});

app.MapPost("/logout", (HttpContext httpContext) =>
{
    httpContext.Response.Cookies.Delete(CookieAcceso.Backoffice);
    return Results.Redirect("/login");
});

app.MapApisRepartidor();
app.MapAvisosTecnicos();
app.MapHub<HubAvisosOperador>(HubAvisosOperador.Ruta).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

public partial class Program;
