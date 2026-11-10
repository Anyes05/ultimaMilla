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
    public void GetMigrations_IncluyeLasNueveMigracionesConAtributos()
    {
        // Proveedor relacional requerido por GetMigrations; no abre conexión real.
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=migraciones_descubrimiento;Username=x;Password=x")
            .Options;

        using var contexto = new PlataformaDbContext(options);
        var ids = contexto.Database.GetMigrations().ToArray();

        Assert.Equal(9, ids.Length);
        Assert.Contains("20261013023329_InitialCreate", ids);
        Assert.Contains("20261023010000_SeguimientoUnicoComprobanteYZona", ids);
        Assert.Contains("20261023020000_OutboxEnvioEstadoCambiado", ids);
        Assert.Contains("20261023030000_DispositivosRepartidor", ids);
        Assert.Contains("20261023040000_EventosSincronizacionProcesados", ids);
        Assert.Contains("20261023234302_WorkerAvisosYFallidos", ids);
        Assert.Contains("20261029210000_EvidenciaCargaMovil", ids);
        Assert.Contains("20261102120000_BultosParcialTarifaYOutboxTrace", ids);
        Assert.Contains("20261110024141_IdentityAspNetUsers", ids);
    }

    [Fact]
    public void AtributosMigration_EstanEnLasMigracionesPosterioresAlInitial()
    {
        var assembly = typeof(PlataformaDbContext).Assembly;
        var esperadas = new[]
        {
            "20261023010000_SeguimientoUnicoComprobanteYZona",
            "20261023020000_OutboxEnvioEstadoCambiado",
            "20261023030000_DispositivosRepartidor",
            "20261023040000_EventosSincronizacionProcesados",
            "20261023234302_WorkerAvisosYFallidos",
            "20261029210000_EvidenciaCargaMovil",
            "20261102120000_BultosParcialTarifaYOutboxTrace",
            "20261110024141_IdentityAspNetUsers"
        };

        foreach (var id in esperadas)
        {
            var tipo = assembly.GetTypes().Single(t =>
                t.GetCustomAttribute<MigrationAttribute>()?.Id == id);
            Assert.NotNull(tipo.GetCustomAttribute<DbContextAttribute>());
        }
    }

    [Fact]
    public void EvidenciaCargaMovil_GeneraCambioAditivoYSnapshotSinPendientes()
    {
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=migraciones_descubrimiento;Username=x;Password=x")
            .Options;
        using var contexto = new PlataformaDbContext(options);
        Assert.False(contexto.Database.HasPendingModelChanges());
        var sql = contexto.GetService<IMigrator>().GenerateScript(
            "20261023234302_WorkerAvisosYFallidos", "20261029210000_EvidenciaCargaMovil");
        Assert.Contains("ADD \"CodigoEscaneado\" text", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
    }

    [Fact]
    public void BultosParcial_GeneraTablaYBUltosYColumnasDeGate()
    {
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=migraciones_descubrimiento;Username=x;Password=x")
            .Options;
        using var contexto = new PlataformaDbContext(options);
        var sql = contexto.GetService<IMigrator>().GenerateScript(
            "20261029210000_EvidenciaCargaMovil", "20261102120000_BultosParcialTarifaYOutboxTrace");
        Assert.Contains("CREATE TABLE \"Bultos\"", sql);
        Assert.Contains("ReglaTarifariaId", sql);
        Assert.Contains("TraceParent", sql);
        Assert.Contains("AdmiteEntregaParcial", sql);
        Assert.Contains("Version", sql);
    }
}
