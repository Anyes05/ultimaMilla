using System.Collections.Concurrent;

namespace Plataforma.Application;

/// <summary>Limita intentos fallidos de login del repartidor (en memoria de proceso).</summary>
public sealed class ProtectorLoginRepartidor
{
    public const int MaximoIntentos = 5;
    public static readonly TimeSpan VentanaBloqueo = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, RegistroIntentos> _intentos = new(StringComparer.OrdinalIgnoreCase);

    public bool EstaBloqueado(string usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario))
            return false;

        if (!_intentos.TryGetValue(Normalizar(usuario), out var registro))
            return false;

        if (registro.BloqueadoHasta is DateTimeOffset hasta && hasta > DateTimeOffset.UtcNow)
            return true;

        if (registro.BloqueadoHasta is not null && registro.BloqueadoHasta <= DateTimeOffset.UtcNow)
            _intentos.TryRemove(Normalizar(usuario), out _);

        return false;
    }

    public void RegistrarFallo(string usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario))
            return;

        var clave = Normalizar(usuario);
        _intentos.AddOrUpdate(
            clave,
            _ => new RegistroIntentos(1, null),
            (_, actual) =>
            {
                if (actual.BloqueadoHasta is DateTimeOffset hasta && hasta > DateTimeOffset.UtcNow)
                    return actual;

                var fallos = actual.Fallos + 1;
                return fallos >= MaximoIntentos
                    ? new RegistroIntentos(fallos, DateTimeOffset.UtcNow.Add(VentanaBloqueo))
                    : new RegistroIntentos(fallos, null);
            });
    }

    public void RegistrarExito(string usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario))
            return;

        _intentos.TryRemove(Normalizar(usuario), out _);
    }

    private static string Normalizar(string usuario) => usuario.Trim().ToLowerInvariant();

    private sealed record RegistroIntentos(int Fallos, DateTimeOffset? BloqueadoHasta);
}
