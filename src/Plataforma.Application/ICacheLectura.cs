namespace Plataforma.Application;

public interface ICacheLectura
{
    Task<string?> LeerAsync(string clave, CancellationToken cancellationToken = default);

    Task GuardarAsync(string clave, string valor, TimeSpan vigencia, CancellationToken cancellationToken = default);

    Task QuitarAsync(string clave, CancellationToken cancellationToken = default);

    EstadisticasCache Estadisticas();
}
