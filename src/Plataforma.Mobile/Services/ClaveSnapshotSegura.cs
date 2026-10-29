using System.Security.Cryptography;

namespace Plataforma.Mobile.Services;

public static class ClaveSnapshotSegura
{
    private static readonly Lazy<byte[]> Clave = new(() => Task.Run(async () =>
    {
        const string nombre = "repartidor.sqlite.aesgcm.v1";
        var valor = await SecureStorage.Default.GetAsync(nombre);
        if (valor is not null) return Convert.FromBase64String(valor);
        var clave = RandomNumberGenerator.GetBytes(32);
        await SecureStorage.Default.SetAsync(nombre, Convert.ToBase64String(clave));
        return clave;
    }).GetAwaiter().GetResult());

    public static byte[] Obtener()
    {
        try { return Clave.Value; }
        catch (Exception ex) { throw new InvalidOperationException("Almacenamiento seguro no disponible. No se reinicio la base local.", ex); }
    }
}
