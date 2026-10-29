using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Plataforma.Avisos;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;
using Plataforma.PortalComercio;
using Plataforma.PortalComercio.Components;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAutenticacionJwt(builder.Configuration, "/login", CookieAcceso.Portal);
// Suscripción a avisos e historial de entregas del comercio (letra 6.9).
builder.Services.AddAvisos(builder.Configuration);

var keysPath = Path.Combine(builder.Environment.ContentRootPath, "keys");
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("Plataforma.PortalComercio");
var opcionesRedis = CacheRedis.CrearOpciones(builder.Configuration);
if (opcionesRedis is null)
{
    Directory.CreateDirectory(keysPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}
else
{
    dataProtection.PersistKeysToStackExchangeRedis(
        ConnectionMultiplexer.Connect(opcionesRedis),
        "plataforma:portal:data-protection");
}

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

var app = builder.Build();

if (string.Equals(builder.Configuration["Database:MigrateOnStartup"], "true", StringComparison.OrdinalIgnoreCase))
{
    await app.Services.MigrarBaseDeDatosAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

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

    if (resultado is ResultadoInicio.Ok ok && ok.Sesion.Rol == RolUsuario.Comercio)
    {
        request.HttpContext.Response.Cookies.Append(
            CookieAcceso.Portal,
            emisor.Emitir(ok.Sesion),
            CookieAcceso.Opciones);
        return Results.Redirect("/");
    }

    return Results.Redirect("/login?error=1");
});

app.MapPost("/logout", (HttpContext httpContext) =>
{
    httpContext.Response.Cookies.Delete(CookieAcceso.Portal);
    return Results.Redirect("/login");
});

// Reenvío de un aviso fallido desde el panel del comercio. El comercio sale del usuario autenticado:
// solo puede reenviar sus propios avisos.
app.MapPost("/avisos/reenviar", async (HttpContext contexto, IAntiforgery antiforgery, ServicioAvisos avisos, ILogger<Program> logger) =>
{
    await antiforgery.ValidateRequestAsync(contexto);

    if (!AccesoActual.Intentar(contexto.User, out var acceso) ||
        acceso.Rol != Roles.Comercio ||
        acceso.ComercioId is not Guid comercioId)
        return Results.Redirect("/login");

    var formulario = await contexto.Request.ReadFormAsync(contexto.RequestAborted);
    if (!Guid.TryParse(formulario["entregaId"], out var entregaId))
        return Results.Redirect("/avisos?error=" + Uri.EscapeDataString("Aviso inválido."));

    try
    {
        await avisos.ReintentarAsync(entregaId, acceso.OperadorId, comercioId, contexto.RequestAborted);
        return Results.Redirect("/avisos?ok=reenviado");
    }
    catch (InvalidOperationException ex)
    {
        return Results.Redirect("/avisos?error=" + Uri.EscapeDataString(TextosUsuario.Error(ex)));
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        // Quedó Pendiente: el barrido del worker lo envía aunque la cola no haya respondido ahora.
        logger.LogWarning(ex, "Reenvío {EntregaId} guardado pero no publicado.", entregaId);
        return Results.Redirect("/avisos?ok=reenviado");
    }
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
