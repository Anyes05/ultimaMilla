using Plataforma.Domain;

namespace Plataforma.Application;

public interface IRecepcionRepositorio
{
    Task AgregarAsync(RecepcionEnvio recepcion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiscrepanciaRecepcion>> ListarDiscrepanciasAsync(
        Guid operadorId,
        int limite,
        CancellationToken cancellationToken = default);
    Task GuardarAsync(CancellationToken cancellationToken = default);
}

/// <summary>RF 10: recepción en depósito con registro de discrepancias de bultos.</summary>
public sealed class RecibirEnvioEnDeposito
{
    private readonly IEnvioRepositorio _envios;
    private readonly IBultoRepositorio _bultos;
    private readonly IRecepcionRepositorio _recepciones;
    private readonly CambiarEstadoEnvio _cambiarEstado;

    public RecibirEnvioEnDeposito(
        IEnvioRepositorio envios,
        IBultoRepositorio bultos,
        IRecepcionRepositorio recepciones,
        CambiarEstadoEnvio cambiarEstado)
    {
        _envios = envios;
        _bultos = bultos;
        _recepciones = recepciones;
        _cambiarEstado = cambiarEstado;
    }

    public async Task<ResultadoRecepcion> EjecutarAsync(
        Guid operadorId,
        Guid envioId,
        int bultosRecibidos,
        string usuario,
        string observaciones = "",
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? bultosFaltantes = null)
    {
        var envio = await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El envío no existe para ese operador.");

        if (envio.Estado != EstadoEnvio.Admitido)
            throw new InvalidOperationException($"Solo se reciben envíos Admitido (actual: {envio.Estado}).");

        var bultos = await _bultos.ListarPorEnvioAsync(operadorId, envioId, cancellationToken);
        var esperados = Math.Max(1, bultos.Count);

        var recepcion = RecepcionEnvio.Registrar(
            Guid.NewGuid(),
            operadorId,
            envioId,
            esperados,
            bultosRecibidos,
            usuario,
            observaciones,
            bultosFaltantes);

        await _recepciones.AgregarAsync(recepcion, cancellationToken);

        var motivo = recepcion.HayDiscrepancia
            ? $"Recepción con discrepancia: esperados {esperados}, recibidos {bultosRecibidos}. {observaciones}"
            : "Recepción en depósito";

        var transicion = await _cambiarEstado.EjecutarAsync(
            envioId,
            operadorId,
            EstadoEnvio.EnDeposito,
            usuario,
            motivo,
            cancellationToken);

        await _recepciones.GuardarAsync(cancellationToken);

        return new ResultadoRecepcion(
            envio.Id,
            recepcion.Id,
            recepcion.HayDiscrepancia,
            esperados,
            bultosRecibidos,
            transicion.EventId);
    }

    /// <summary>
    /// RF 10 por escaneo: el operario escanea los códigos de los bultos que llegaron (de uno o varios envíos). Cada
    /// envío pendiente se recibe con lo escaneado y queda registrado qué bultos declarados faltaron. Escanear el número
    /// de seguimiento recibe el envío completo (envíos sin bultos o etiqueta del envío). Los códigos que no son de
    /// ningún envío pendiente se informan y no se reciben.
    /// </summary>
    public async Task<ResultadoEscaneoRecepcion> RecibirPorEscaneoAsync(
        Guid operadorId,
        IReadOnlyList<string> codigos,
        string usuario,
        string observaciones = "",
        CancellationToken cancellationToken = default)
    {
        var limpios = codigos
            .Select(codigo => codigo.Trim())
            .Where(codigo => codigo.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (limpios.Count == 0)
            throw new ArgumentException("Escaneá al menos un código.");

        var escaneadosPorEnvio = new Dictionary<Guid, HashSet<Guid>>();
        var completos = new HashSet<Guid>();
        var noReconocidos = new List<string>();
        foreach (var codigo in limpios)
        {
            // Los códigos se generan en mayúsculas; tipeados a mano pueden venir en minúsculas.
            var bultoEncontrado = await _bultos.ObtenerPorCodigoAsync(operadorId, codigo, cancellationToken)
                ?? await _bultos.ObtenerPorCodigoAsync(operadorId, codigo.ToUpperInvariant(), cancellationToken);
            if (bultoEncontrado is { } bulto)
            {
                var envioDelBulto = await _envios.ObtenerPorOperadorAsync(bulto.EnvioId, operadorId, cancellationToken);
                if (envioDelBulto?.Estado != EstadoEnvio.Admitido)
                {
                    noReconocidos.Add($"{codigo} (su envío ya fue recibido o no está pendiente)");
                    continue;
                }

                if (!escaneadosPorEnvio.TryGetValue(bulto.EnvioId, out var bultos))
                    escaneadosPorEnvio[bulto.EnvioId] = bultos = [];
                bultos.Add(bulto.Id);
                continue;
            }

            var envio = await _envios.ObtenerPorNumeroAsync(codigo, cancellationToken)
                ?? await _envios.ObtenerPorNumeroAsync(codigo.ToUpperInvariant(), cancellationToken);
            if (envio is not null && envio.OperadorId == operadorId && envio.Estado == EstadoEnvio.Admitido)
                completos.Add(envio.Id);
            else
                noReconocidos.Add(codigo);
        }

        var recibidos = new List<RecepcionEscaneada>();
        var errores = new List<string>();
        foreach (var envioId in escaneadosPorEnvio.Keys.Union(completos))
        {
            var envio = (await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken))!;
            var declarados = await _bultos.ListarPorEnvioAsync(operadorId, envioId, cancellationToken);
            var esperados = Math.Max(1, declarados.Count);
            var faltantes = completos.Contains(envioId) || declarados.Count == 0
                ? []
                : declarados.Where(bulto => !escaneadosPorEnvio[envioId].Contains(bulto.Id)).Select(bulto => bulto.Codigo).OrderBy(c => c).ToList();
            var cantidad = esperados - faltantes.Count;
            var nota = faltantes.Count == 0
                ? observaciones
                : string.IsNullOrWhiteSpace(observaciones)
                    ? $"Faltan: {string.Join(", ", faltantes)}"
                    : $"{observaciones.Trim()} (faltan: {string.Join(", ", faltantes)})";
            try
            {
                await EjecutarAsync(operadorId, envioId, cantidad, usuario, nota, cancellationToken, faltantes);
                recibidos.Add(new RecepcionEscaneada(envio.NumeroSeguimiento, esperados, cantidad, faltantes));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                errores.Add($"{envio.NumeroSeguimiento}: {ex.Message}");
            }
        }

        return new ResultadoEscaneoRecepcion(recibidos, noReconocidos, errores);
    }
}

/// <summary>Lo recibido de un envío al escanear: cuántos bultos llegaron y cuáles faltan.</summary>
public sealed record RecepcionEscaneada(
    string NumeroSeguimiento,
    int BultosEsperados,
    int BultosRecibidos,
    IReadOnlyList<string> Faltantes);

/// <summary>Resultado de una tanda de escaneo: envíos recibidos, códigos que no son de ningún envío pendiente y errores.</summary>
public sealed record ResultadoEscaneoRecepcion(
    IReadOnlyList<RecepcionEscaneada> Recibidos,
    IReadOnlyList<string> NoReconocidos,
    IReadOnlyList<string> Errores);

public sealed record DiscrepanciaRecepcion(
    string NumeroSeguimiento,
    string Destinatario,
    int BultosEsperados,
    int BultosRecibidos,
    string Observaciones,
    string Usuario,
    DateTimeOffset Fecha,
    string BultosFaltantes = "");

public sealed record ResultadoRecepcion(
    Guid EnvioId,
    Guid RecepcionId,
    bool HayDiscrepancia,
    int BultosEsperados,
    int BultosRecibidos,
    Guid? EventId);
