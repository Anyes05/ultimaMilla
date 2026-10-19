namespace Plataforma.Mobile.Services;

public sealed class TokenRepartidorSeguro : ITokenRepartidor
{
    private const string Key = "repartidor-api-token";
    public async Task<string?> LeerAsync()
    {
        try { return await SecureStorage.Default.GetAsync(Key); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException
            or IOException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidOperationException("No se pudo leer el token del almacenamiento seguro. Inicia sesion nuevamente.", ex);
        }
    }
    public async Task GuardarAsync(string token)
    {
        try { await SecureStorage.Default.SetAsync(Key, token); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException
            or IOException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidOperationException("No se pudo guardar la sesion en el almacenamiento seguro.", ex);
        }
    }
    public void Borrar() => SecureStorage.Default.Remove(Key);
}
