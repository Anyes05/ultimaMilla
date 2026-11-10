namespace Plataforma.Application;

public sealed record EnvioImportacionItem(
    string ClaveExterna,
    string Destinatario,
    string DireccionEntrega,
    string EmailDestinatario = "",
    decimal PesoKg = 1,
    decimal DistanciaKm = 0,
    bool Fragil = false,
    bool Urgente = false,
    int CantidadBultos = 1,
    Guid? ZonaId = null,
    Guid? FranjaId = null,
    decimal VolumenCm3 = 0);

public sealed record ResultadoImportacionItem(
    string ClaveExterna,
    bool Creado,
    bool Reutilizado,
    Guid? EnvioId,
    string? Error);

/// <summary>Importación lote de envíos con idempotencia por clave externa del comercio.</summary>
public sealed class ImportarEnvios
{
    private readonly AdmitirEnvio _admitir;
    private readonly IEnvioRepositorio _envios;

    public ImportarEnvios(AdmitirEnvio admitir, IEnvioRepositorio envios)
    {
        _admitir = admitir;
        _envios = envios;
    }

    public async Task<IReadOnlyList<ResultadoImportacionItem>> EjecutarAsync(
        Guid operadorId,
        Guid comercioId,
        IReadOnlyList<EnvioImportacionItem> items,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
            throw new ArgumentException("La importación necesita al menos un ítem.", nameof(items));

        var resultados = new List<ResultadoImportacionItem>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ClaveExterna))
            {
                resultados.Add(new ResultadoImportacionItem("", false, false, null, "La clave externa es requerida."));
                continue;
            }

            var clave = item.ClaveExterna.Trim();
            try
            {
                var previo = await _envios.ObtenerPorClaveExternaAsync(operadorId, comercioId, clave, cancellationToken);
                var envio = await _admitir.EjecutarAsync(
                    operadorId,
                    comercioId,
                    item.Destinatario,
                    item.DireccionEntrega,
                    item.EmailDestinatario,
                    item.PesoKg,
                    item.DistanciaKm,
                    item.Fragil,
                    item.Urgente,
                    item.CantidadBultos,
                    item.ZonaId,
                    item.FranjaId,
                    item.VolumenCm3,
                    clave,
                    cancellationToken);

                var reutilizado = previo is not null;
                resultados.Add(new ResultadoImportacionItem(
                    clave,
                    !reutilizado,
                    reutilizado,
                    envio.Id,
                    null));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
            {
                resultados.Add(new ResultadoImportacionItem(clave, false, false, null, ex.Message));
            }
        }

        return resultados;
    }
}
