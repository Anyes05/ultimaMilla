using System.Security.Cryptography;
using System.Text;

namespace Plataforma.Mobile.Data;

public interface ISnapshotCipher
{
    string Cifrar(string texto);
    string Descifrar(string texto);
}

public sealed class SnapshotCipher(Func<byte[]> obtenerClave) : ISnapshotCipher
{
    public string Cifrar(string texto)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(texto);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(obtenerClave(), 16);
        aes.Encrypt(nonce, plain, cipher, tag, "EstadoDemo:1:v1"u8);
        return "aesgcm1:" + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public string Descifrar(string texto)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(texto["aesgcm1:".Length..]); }
        catch (FormatException ex) { throw new CryptographicException("Snapshot cifrado invalido.", ex); }
        if (bytes.Length < 28) throw new CryptographicException("Snapshot truncado.");
        var plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(obtenerClave(), 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, "EstadoDemo:1:v1"u8);
        return Encoding.UTF8.GetString(plain);
    }
}
