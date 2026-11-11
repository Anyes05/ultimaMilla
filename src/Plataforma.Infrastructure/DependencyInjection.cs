using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("La cadena de conexion 'Default' es requerida.");

        services.AddHttpContextAccessor();
        services.AddScoped<IOperadorActual, OperadorActualHttp>();
        services.AddScoped<OperadorSaveChangesInterceptor>();

        services.AddDbContext<PlataformaDbContext>((proveedor, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(proveedor.GetRequiredService<OperadorSaveChangesInterceptor>());
        });

        // Data Protection mínimo: Identity/UserManager y hosts de test que solo llaman
        // AddInfrastructure (FlujoEntregaPostgresTests) no registran Redis DP.
        services.AddDataProtection();

        // Sin AddDefaultTokenProviders: el login Opción A solo usa password hasher.
        // Esos providers exigen IDataProtectionProvider y fallaban el CI de integración.
        services
            .AddIdentityCore<UsuarioIdentity>(opciones =>
            {
                opciones.User.RequireUniqueEmail = false;
                opciones.Password.RequiredLength = 6;
                opciones.Password.RequireDigit = false;
                opciones.Password.RequireLowercase = false;
                opciones.Password.RequireUppercase = false;
                opciones.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<PlataformaDbContext>();

        services.AddCacheRedis(configuration);
        services.Configure<OpcionesRabbitMq>(configuration.GetSection(OpcionesRabbitMq.Seccion));

        var rabbitHost = configuration[$"{OpcionesRabbitMq.Seccion}:Host"];
        if (!string.IsNullOrWhiteSpace(rabbitHost))
        {
            services.AddSingleton<IPublicadorMensajes, PublicadorMensajesRabbitMq>();
            services.AddHostedService<OutboxPublicadorBackground>();
        }
        else
        {
            services.AddSingleton<IPublicadorMensajes, PublicadorMensajesNulo>();
        }

        services.AddScoped<IEnvioRepositorio, EnvioRepositorio>();
        services.AddScoped<IBultoRepositorio, BultoRepositorio>();
        services.AddScoped<IUsuarioRepositorio, UsuarioRepositorio>();
        services.AddScoped<IOperadorRepositorio, OperadorRepositorio>();
        services.AddScoped<IRutaRepositorio, RutaRepositorio>();
        services.AddScoped<IRepartidorRepositorio, RepartidorRepositorio>();
        services.AddScoped<ITarifaRepositorio, TarifaRepositorio>();
        services.AddScoped<IPruebaDeEntregaRepositorio, PruebaDeEntregaRepositorio>();
        services.AddScoped<IIntentoEntregaRepositorio, IntentoEntregaRepositorio>();
        services.AddScoped<IOutboxRepositorio, OutboxRepositorio>();
        services.AddScoped<IDispositivoRepositorio, DispositivoRepositorio>();
        services.AddScoped<IEventoSincronizacionRepositorio, EventoSincronizacionRepositorio>();
        services.AddScoped<IHistorialEstadoRepositorio, HistorialEstadoRepositorio>();
        services.AddScoped<IReglaOperativaRepositorio, ReglaOperativaRepositorio>();
        services.AddScoped<IBanderaRepositorio, BanderaRepositorio>();
        services.AddScoped<IRetiroRepositorio, RetiroRepositorio>();
        services.AddScoped<IRecepcionRepositorio, RecepcionRepositorio>();
        services.AddScoped<IPosicionRepositorio, PosicionRepositorio>();
        services.AddScoped<ICierreDiarioRepositorio, CierreDiarioRepositorio>();
        services.AddScoped<ListarCierresDiarios>();
        services.AddScoped<RegistrarPosiciones>();
        services.AddScoped<ObtenerFlota>();
        services.AddScoped<ICoberturaRepositorio, CoberturaRepositorio>();
        services.AddScoped<IComercioRepositorio, ComercioRepositorio>();
        services.AddScoped<IVerificadorClave, HashClave>();
        services.AddScoped<IAutenticacionIdentity, AutenticacionIdentity>();
        services.AddScoped<IEmisorToken, EmisorTokenJwt>();
        services.AddSingleton<ProtectorLoginRepartidor>();
        services.AddScoped<ConfiguracionOperador>();
        services.AddScoped<CalcularTarifa>();
        services.AddScoped<GestionTarifas>();
        services.AddScoped<GestionReglasOperativas>();
        services.AddScoped<GestionBanderas>();
        services.AddScoped<IBanderasOperador>(sp => sp.GetRequiredService<GestionBanderas>());
        services.AddScoped<GestionRetiros>();
        services.AddScoped<GestionCobertura>();
        services.AddScoped<GestionComercios>();
        services.AddScoped<GestionUsuarios>();
        services.AddScoped<GestionRepartidores>();
        services.AddScoped<RecibirEnvioEnDeposito>();
        services.AddScoped<RendirBulto>();
        services.AddScoped<AdmitirEnvio>();
        services.AddScoped<ImportarEnvios>();
        services.AddScoped<ILiquidacionRepositorio, LiquidacionRepositorio>();
        services.AddScoped<GestionLiquidaciones>();
        services.AddScoped<ObtenerReportesGestion>();
        services.AddScoped<ListarEnviosEnRiesgo>();
        services.AddScoped<ActualizarMarcaOperador>();
        services.AddScoped<RegistrarOutboxEstado>();
        services.AddScoped<PublicarOutboxPendientes>();
        services.AddScoped<CambiarEstadoEnvio>();
        services.AddScoped<ITransicionEnvio>(proveedor => proveedor.GetRequiredService<CambiarEstadoEnvio>());
        services.AddScoped<IniciarSesion>();
        services.AddScoped<IniciarSesionRepartidor>();
        services.AddScoped<IniciarRuta>();
        services.AddScoped<ListarEnvios>();
        services.AddScoped<ListarRepartidoresActivos>();
        services.AddScoped<ListarRutasDelDia>();
        services.AddScoped<DespacharRuta>();
        services.AddScoped<ReasignarEnvio>();
        services.AddScoped<ObtenerRutasDeHoy>();
        services.AddScoped<SincronizarEntregas>();
        services.AddScoped<ObtenerSeguimientoEnvio>();
        services.AddScoped<ObtenerDetalleSeguimiento>();
        services.AddScoped<SolicitarReprogramacion>();
        services.AddScoped<ObtenerComprobantesEnvio>();
        services.AddScoped<RegistrarDispositivoRepartidor>();
        services.AddScoped<QuitarDispositivoRepartidor>();

        return services;
    }

    public static async Task MigrarBaseDeDatosAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        await context.Database.MigrateAsync();
        await SemillaDatos.AplicarAsync(context);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<UsuarioIdentity>>();
        await SemillaIdentidad.SincronizarAsync(users, context);
    }
}
