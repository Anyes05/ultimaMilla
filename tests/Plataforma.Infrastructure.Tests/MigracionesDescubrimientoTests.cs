using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>
/// Evita el fallo de producción donde migraciones manuales sin [Migration]/[DbContext]
/// no se aplican y el Backoffice parte por columnas/tablas inexistentes.
/// </summary>
public class MigracionesDescubrimientoTests
{
    [Fact]
    public void GetMigrations_IncluyeLasCincoMigracionesConAtributos()
    {
        // Proveedor relacional requerido por GetMigrations; no abre conexión real.
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=migraciones_descubrimiento;Username=x;Password=x")
            .Options;

        using var contexto = new PlataformaDbContext(options);
        var ids = contexto.Database.GetMigrations().ToArray();

        Assert.Equal(5, ids.Length);
        Assert.Contains("20261013023329_InitialCreate", ids);
        Assert.Contains("20261023010000_SeguimientoUnicoComprobanteYZona", ids);
        Assert.Contains("20261023020000_OutboxEnvioEstadoCambiado", ids);
        Assert.Contains("20261023030000_DispositivosRepartidor", ids);
        Assert.Contains("20261023040000_EventosSincronizacionProcesados", ids);
    }

    [Fact]
    public void AtributosMigration_EstanEnLasCuatroMigracionesDel23()
    {
        var assembly = typeof(PlataformaDbContext).Assembly;
        var esperadas = new[]
        {
            "20261023010000_SeguimientoUnicoComprobanteYZona",
            "20261023020000_OutboxEnvioEstadoCambiado",
            "20261023030000_DispositivosRepartidor",
            "20261023040000_EventosSincronizacionProcesados"
        };

        foreach (var id in esperadas)
        {
            var tipo = assembly.GetTypes().Single(t =>
                t.GetCustomAttribute<MigrationAttribute>()?.Id == id);
            Assert.NotNull(tipo.GetCustomAttribute<DbContextAttribute>());
        }
    }
}
