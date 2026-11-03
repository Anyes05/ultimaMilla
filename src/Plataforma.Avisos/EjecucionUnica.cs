using Microsoft.EntityFrameworkCore;

namespace Plataforma.Avisos;

/// <summary>
/// Tareas programadas que no deben correr a la vez en varias réplicas del worker (letra 6.13 y aclaración 3.1).
/// Usa un bloqueo consultivo de Postgres atado a una transacción: la réplica que lo obtiene ejecuta, las demás
/// saltean esa vuelta. Si el proceso muere, Postgres libera el bloqueo solo al cortarse la conexión.
/// </summary>
public static class EjecucionUnica
{
    /// <summary>Barrido de entregas de avisos pendientes ("AVISBARR" en hexadecimal).</summary>
    public const long BarridoEntregas = 0x4156495342415252;

    /// <summary>
    /// Ejecuta <paramref name="accion"/> solo si ninguna otra instancia tiene el bloqueo <paramref name="clave"/>.
    /// Devuelve false si otra instancia lo está ejecutando.
    /// </summary>
    public static async Task<bool> IntentarAsync(
        AvisosDbContext db,
        long clave,
        Func<CancellationToken, Task> accion,
        CancellationToken cancellationToken)
    {
        await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);

        var obtenido = await db.Database
            .SqlQuery<bool>($"""SELECT pg_try_advisory_xact_lock({clave}) AS "Value" """)
            .SingleAsync(cancellationToken);
        if (!obtenido)
            return false;

        await accion(cancellationToken);
        await transaccion.CommitAsync(cancellationToken);
        return true;
    }
}
