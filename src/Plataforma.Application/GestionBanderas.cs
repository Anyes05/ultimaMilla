using System.Text.Json;
using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class GestionBanderas : IBanderasOperador
{
    private readonly IBanderaRepositorio _banderas;
    private readonly ICacheLectura _cache;

    public GestionBanderas(IBanderaRepositorio banderas, ICacheLectura cache)
    {
        _banderas = banderas;
        _cache = cache;
    }

    public async Task<bool> EstaActivaAsync(
        Guid operadorId,
        string clave,
        CancellationToken cancellationToken = default)
    {
        if (!CatalogoBanderas.EsConocida(clave))
            return false;

        var mapa = await LeerMapaAsync(operadorId, cancellationToken);
        return mapa.TryGetValue(clave, out var activa)
            ? activa
            : CatalogoBanderas.ValorPorDefecto(clave);
    }

    public async Task<IReadOnlyList<BanderaVista>> ListarPorOperadorAsync(
        Guid operadorId,
        CancellationToken cancellationToken = default)
    {
        var mapa = await LeerMapaAsync(operadorId, cancellationToken);
        return CatalogoBanderas.Todas
            .Select(def => new BanderaVista(
                operadorId,
                def.Clave,
                def.Descripcion,
                mapa.TryGetValue(def.Clave, out var activa) ? activa : def.ActivaPorDefecto))
            .ToList();
    }

    /// <summary>Vista para administrador de plataforma: todos los operadores × catálogo.</summary>
    public async Task<IReadOnlyList<BanderaVista>> ListarTodasAsync(
        IReadOnlyList<Guid> operadoresId,
        CancellationToken cancellationToken = default)
    {
        var resultado = new List<BanderaVista>();
        foreach (var operadorId in operadoresId)
            resultado.AddRange(await ListarPorOperadorAsync(operadorId, cancellationToken));
        return resultado;
    }

    public async Task CambiarAsync(
        Guid operadorId,
        string clave,
        bool activa,
        string usuario,
        CancellationToken cancellationToken = default)
    {
        if (!CatalogoBanderas.EsConocida(clave))
            throw new ArgumentException($"Bandera desconocida: {clave}", nameof(clave));
        if (string.IsNullOrWhiteSpace(usuario))
            throw new ArgumentException("El usuario es requerido.", nameof(usuario));

        var actual = await _banderas.ObtenerAsync(operadorId, clave, cancellationToken);
        if (actual is null)
        {
            actual = BanderaOperador.Crear(operadorId, clave, activa);
            await _banderas.AgregarAsync(actual, cancellationToken);
        }
        else
        {
            actual.Cambiar(activa);
        }

        await _banderas.AgregarCambioAsync(
            CambioBandera.Registrar(Guid.NewGuid(), operadorId, clave, activa, usuario),
            cancellationToken);
        await _banderas.GuardarAsync(cancellationToken);
        await _cache.QuitarAsync(ClavesCache.Banderas(operadorId), cancellationToken);
    }

    public Task<IReadOnlyList<CambioBandera>> HistorialAsync(
        Guid? operadorId,
        CancellationToken cancellationToken = default) =>
        _banderas.HistorialAsync(operadorId, cancellationToken);

    private async Task<Dictionary<string, bool>> LeerMapaAsync(
        Guid operadorId,
        CancellationToken cancellationToken)
    {
        var clave = ClavesCache.Banderas(operadorId);
        var cacheado = await _cache.LeerAsync(clave, cancellationToken);
        if (cacheado is not null)
        {
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(cacheado)
                ?? new Dictionary<string, bool>(StringComparer.Ordinal);
        }

        var filas = await _banderas.ListarPorOperadorAsync(operadorId, cancellationToken);
        var mapa = filas.ToDictionary(f => f.Clave, f => f.Activa, StringComparer.Ordinal);
        await _cache.GuardarAsync(
            clave,
            JsonSerializer.Serialize(mapa),
            CatalogoBanderas.VigenciaCache,
            cancellationToken);
        return mapa;
    }
}

public sealed record BanderaVista(Guid OperadorId, string Clave, string Descripcion, bool Activa);
