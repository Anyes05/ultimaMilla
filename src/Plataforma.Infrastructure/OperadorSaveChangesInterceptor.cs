using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

/// <summary>
/// Impide escribir datos de otro operador. Si OperadorId viene vacío en un alta, lo completa.
/// </summary>
public sealed class OperadorSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IOperadorActual _operadorActual;

    public OperadorSaveChangesInterceptor(IOperadorActual operadorActual)
    {
        _operadorActual = operadorActual;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Aplicar(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Aplicar(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Aplicar(DbContext? context)
    {
        if (context is null || _operadorActual.OperadorId is not Guid operadorId)
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var propiedad = entry.Metadata.FindProperty("OperadorId");
            if (propiedad is null || propiedad.ClrType != typeof(Guid))
            {
                if (entry.Entity is Domain.Operador && entry.State != EntityState.Detached)
                {
                    var id = (Guid)entry.Property("Id").CurrentValue!;
                    if (id != operadorId)
                        throw new InvalidOperationException("No se pueden modificar datos de otro operador.");
                }

                continue;
            }

            var actual = (Guid)entry.Property("OperadorId").CurrentValue!;

            if (entry.State == EntityState.Added)
            {
                if (actual == Guid.Empty)
                {
                    entry.Property("OperadorId").CurrentValue = operadorId;
                    continue;
                }

                if (actual != operadorId)
                    throw new InvalidOperationException("No se pueden crear datos para otro operador.");
                continue;
            }

            if (actual != operadorId)
                throw new InvalidOperationException("No se pueden modificar datos de otro operador.");

            if (entry.State == EntityState.Modified && entry.Property("OperadorId").IsModified)
            {
                var original = (Guid)entry.Property("OperadorId").OriginalValue!;
                if (original != operadorId || actual != operadorId)
                    throw new InvalidOperationException("No se puede cambiar el operador de un registro.");
            }
        }
    }
}
