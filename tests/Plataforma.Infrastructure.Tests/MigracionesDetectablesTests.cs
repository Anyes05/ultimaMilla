using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class MigracionesDetectablesTests
{
    [Fact]
    public void MigracionesDel23DeOctubre_TienenAtributosDbContextYMigration()
    {
        var assembly = typeof(PlataformaDbContext).Assembly;
        var migraciones = assembly.GetTypes()
            .Where(tipo => typeof(Migration).IsAssignableFrom(tipo) && !tipo.IsAbstract)
            .Select(tipo =>
            {
                var migration = tipo.GetCustomAttribute<MigrationAttribute>();
                var db = tipo.GetCustomAttribute<DbContextAttribute>();
                return (Tipo: tipo.Name, Id: migration?.Id, Contexto: db?.ContextType);
            })
            .ToList();

        Assert.Contains(migraciones, m => m.Id == "20261013023329_InitialCreate");
        Assert.Contains(migraciones, m =>
            m.Id == "20261023010000_SeguimientoUnicoComprobanteYZona" &&
            m.Contexto == typeof(PlataformaDbContext));
        Assert.Contains(migraciones, m =>
            m.Id == "20261023020000_OutboxEnvioEstadoCambiado" &&
            m.Contexto == typeof(PlataformaDbContext));
        Assert.Contains(migraciones, m =>
            m.Id == "20261023030000_DispositivosRepartidor" &&
            m.Contexto == typeof(PlataformaDbContext));
        Assert.Contains(migraciones, m =>
            m.Id == "20261023040000_EventosSincronizacionProcesados" &&
            m.Contexto == typeof(PlataformaDbContext));
        Assert.Contains(migraciones, m =>
            m.Id == "20261023234302_WorkerAvisosYFallidos" &&
            m.Contexto == typeof(PlataformaDbContext));
    }
}
