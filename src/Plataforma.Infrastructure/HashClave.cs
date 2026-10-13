using System.Security.Cryptography;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

public sealed class HashClave : IVerificadorClave
{
    private const int Iteraciones = 100_000;
    private const int Largo = 32;

    public static string Crear(string clave)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(clave, sal, Iteraciones, HashAlgorithmName.SHA256, Largo);
        return $"pbkdf2${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public bool Coincide(string clave, string hashAlmacenado)
    {
        var partes = hashAlmacenado.Split('$');
        if (partes.Length != 4 || partes[0] != "pbkdf2" || !int.TryParse(partes[1], out var iteraciones))
            return false;

        var sal = Convert.FromBase64String(partes[2]);
        var esperado = Convert.FromBase64String(partes[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(clave, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(actual, esperado);
    }
}
