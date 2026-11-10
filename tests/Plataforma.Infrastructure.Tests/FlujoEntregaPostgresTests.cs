using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>
/// Prueba de integración de un flujo crítico completo (letra 6.15) contra PostgreSQL real, con la misma composición
/// de servicios, migraciones y semilla que producción. La base en memoria no valida tipos: aceptaba una fecha con
/// -03:00 que Postgres rechaza (sincronización en 500 el 05/11), por eso este flujo corre en Postgres.
/// En CI la base la levanta el pipeline; en local se omite si no está la variable PRUEBAS_POSTGRES.
/// </summary>
[Trait("Category", "Integracion")]
public sealed class FlujoEntregaPostgresTests : IAsyncLifetime
{
    private const string Repartidor = "alvaro@demo.test";

    private readonly string _base = $"integracion_{Guid.NewGuid():N}";
    private ServiceProvider? _servicios;

    public async Task InitializeAsync()
    {
        var conexion = Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(conexion))
            return;

        // Base propia por ejecución: no toca otras bases del servidor y se borra al terminar.
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = new NpgsqlConnectionStringBuilder(conexion) { Database = _base }.ConnectionString
            })
            .Build();

        _servicios = new ServiceCollection().AddLogging().AddInfrastructure(configuracion).BuildServiceProvider();
        await _servicios.MigrarBaseDeDatosAsync();
    }

    public async Task DisposeAsync()
    {
        if (_servicios is null)
            return;

        using (var scope = _servicios.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PlataformaDbContext>().Database.EnsureDeletedAsync();

        await _servicios.DisposeAsync();
    }

    [PostgresFact]
    public async Task EnvioAdmitidoDespachadoYEntregado_QuedaRegistradoDePuntaAPunta()
    {
        var norte = DatosSemilla.OperadorNorteId;
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid envioId;
        string numero;

        // 1. El comercio admite el envío y el depósito lo recibe.
        using (var scope = Scope())
        {
            var envio = await Servicio<AdmitirEnvio>(scope).EjecutarAsync(
                norte, DatosSemilla.ComercioNorteId, "Destinatario Integración", "Calle 1234", pesoKg: 2m, cantidadBultos: 1);
            envioId = envio.Id;
            numero = envio.NumeroSeguimiento;
            await Servicio<ITransicionEnvio>(scope).EjecutarAsync(envioId, norte, EstadoEnvio.EnDeposito, "norte.operario");
        }

        // 2. El despachador lo asigna a la ruta del repartidor de Norte.
        using (var scope = Scope())
            await Servicio<DespacharRuta>(scope).EjecutarAsync(norte, DatosSemilla.RepartidorNorteId, hoy, [envioId]);

        // 3. La app sincroniza la carga escaneada y después la entrega con foto, firma y GPS.
        var carga = new ComprobanteEntrega(Guid.NewGuid(), envioId, EstadoEnvio.EnTransito, "", "",
            DateTimeOffset.UtcNow, CodigoEscaneado: numero);
        var entrega = new ComprobanteEntrega(Guid.NewGuid(), envioId, EstadoEnvio.Entregado, "Receptor", "Puerta",
            DateTimeOffset.UtcNow, "/9j/2Q==", """[[{"x":1,"y":2},{"x":3,"y":4}]]""", -34.9m, -56.2m);

        Assert.Single((await Sincronizar(hoy, carga)).Aplicados);
        Assert.Single((await Sincronizar(hoy, entrega)).Aplicados);
        // Reintento de la app con el mismo evento (red inestable): se confirma sin duplicar nada.
        Assert.Single((await Sincronizar(hoy, entrega)).Aplicados);

        using (var scope = Scope())
        {
            var db = Servicio<PlataformaDbContext>(scope);
            Assert.Equal(EstadoEnvio.Entregado, (await db.Envios.SingleAsync(e => e.Id == envioId)).Estado);
            Assert.Equal(2, await db.PruebasDeEntrega.CountAsync(p => p.EnvioId == envioId));
            Assert.Equal(2, await db.EventosSincronizacionProcesados.CountAsync());
            // Un mensaje por cambio de estado: depósito, ruta, tránsito y entrega (lo que consume el worker).
            Assert.Equal(4, await db.MensajesOutbox.CountAsync(m => m.EnvioId == envioId && m.Tipo == MensajeOutbox.TipoEnvioEstadoCambiado));
            // El alta guarda el evento y los bultos en la misma transacción que el envío.
            Assert.Equal(1, await db.MensajesOutbox.CountAsync(m => m.EnvioId == envioId && m.Tipo == MensajeOutbox.TipoEnvioAdmitido));
            Assert.Equal(1, await db.Bultos.CountAsync(b => b.EnvioId == envioId));
        }

        // 4. El Backoffice ve el comprobante; otro operador no ve el envío.
        using (var scope = Scope())
        {
            var comprobantes = Servicio<ObtenerComprobantesEnvio>(scope);
            var visto = await comprobantes.Ejecutar(Acceso(norte), envioId);
            var deEntrega = Assert.Single(visto!.Comprobantes, c => c.FotoBase64 is not null);
            Assert.Equal(-34.9m, deEntrega.Latitud);
            Assert.Single(deEntrega.Firma);

            Assert.Null(await comprobantes.Ejecutar(Acceso(DatosSemilla.OperadorSurId), envioId));
        }
    }

    private async Task<ResultadoSincronizacion> Sincronizar(DateOnly fecha, ComprobanteEntrega evento)
    {
        using var scope = Scope();
        return await Servicio<SincronizarEntregas>(scope).Ejecutar(
            DatosSemilla.OperadorNorteId, DatosSemilla.RepartidorNorteId, fecha, [evento], Repartidor);
    }

    private static AccesoActual Acceso(Guid operadorId) => new(Guid.NewGuid(), operadorId, null, Roles.Administrador);

    private IServiceScope Scope() => _servicios!.CreateScope();

    private static T Servicio<T>(IServiceScope scope) where T : notnull => scope.ServiceProvider.GetRequiredService<T>();
}

/// <summary>Se omite (en vez de fallar) cuando no hay un PostgreSQL de pruebas configurado.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string Variable = "PRUEBAS_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Sin PostgreSQL de pruebas ({Variable}).";
    }
}
