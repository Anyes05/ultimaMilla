namespace Plataforma.Application;

/// <summary>
/// Operador del usuario autenticado. Null fuera de request (migraciones, seed, design-time).
/// </summary>
public interface IOperadorActual
{
    Guid? OperadorId { get; }
}
