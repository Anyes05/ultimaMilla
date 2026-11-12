using Plataforma.Domain;

namespace Plataforma.Application;

public interface IRecepcionRepositorio
{
    Task AgregarAsync(RecepcionEnvio recepcion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiscrepanciaRecepcion>> ListarDiscrepanciasAsync(
        Guid operadorId,
        int limite,
        CancellationToken cancellationToken = default);
    /// <summary>La recepción más reciente del envío, para registrar bultos que llegan después.</summary>
    Task<RecepcionEnvio?> ObtenerUltimaAsync(Guid operadorId, Guid envioId, CancellationToken cancellationToken = default);
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
    /// RF 10 por escaneo, de a un bulto o varios por vez: cada código marca su bulto como llegado al depósito y el
    /// envío se recibe recién cuando llegaron todos sus bultos (sin discrepancia). Mientras tanto queda "en
    /// recepción". Si el resto no va a llegar, el operario lo decide con <see cref="CerrarConFaltantesAsync"/>.
    /// Escanear el número de seguimiento recibe el envío completo. Un bulto que faltó al cerrar y llega después
    /// completa esa recepción. Los códigos que no son de ningún envío pendiente se informan.
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

        var ahora = DateTimeOffset.UtcNow;
        var enviosTocados = new List<Guid>();
        var completos = new HashSet<Guid>();
        var noReconocidos = new List<string>();
        var llegadas = new List<LlegadaPosterior>();
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
                    // Un bulto que faltó al cerrar la recepción y llega después la completa.
                    if (envioDelBulto is not null
                        && await _recepciones.ObtenerUltimaAsync(operadorId, envioDelBulto.Id, cancellationToken) is { } previa
                        && previa.RegistrarLlegadaPosterior(bulto.Codigo, ahora))
                    {
                        bulto.MarcarRecibidoEnDeposito(ahora);
                        llegadas.Add(new LlegadaPosterior(envioDelBulto.NumeroSeguimiento, bulto.Codigo, previa.Completada));
                        continue;
                    }

                    noReconocidos.Add($"{codigo} (su envío ya fue recibido o no está pendiente)");
                    continue;
                }

                if (!bulto.MarcarRecibidoEnDeposito(ahora))
                {
                    noReconocidos.Add($"{codigo} (ya estaba escaneado)");
                    continue;
                }

                if (!enviosTocados.Contains(bulto.EnvioId))
                    enviosTocados.Add(bulto.EnvioId);
                continue;
            }

            var envio = await _envios.ObtenerPorNumeroAsync(codigo, cancellationToken)
                ?? await _envios.ObtenerPorNumeroAsync(codigo.ToUpperInvariant(), cancellationToken);
            if (envio is not null && envio.OperadorId == operadorId && envio.Estado == EstadoEnvio.Admitido)
            {
                completos.Add(envio.Id);
                if (!enviosTocados.Contains(envio.Id))
                    enviosTocados.Add(envio.Id);
            }
            else
            {
                noReconocidos.Add(codigo);
            }
        }

        // Las marcas de los bultos quedan guardadas aunque el envío siga en recepción.
        await _bultos.GuardarAsync(cancellationToken);

        var recibidos = new List<RecepcionEscaneada>();
        var enCurso = new List<RecepcionEnCurso>();
        var errores = new List<string>();
        foreach (var envioId in enviosTocados)
        {
            var envio = (await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken))!;
            var declarados = await _bultos.ListarPorEnvioAsync(operadorId, envioId, cancellationToken);
            var llegados = declarados.Count(bulto => bulto.RecibidoEnDeposito is not null);
            if (!completos.Contains(envioId) && declarados.Count > 0 && llegados < declarados.Count)
            {
                enCurso.Add(new RecepcionEnCurso(
                    envio.NumeroSeguimiento,
                    llegados,
                    declarados.Count,
                    declarados.Where(bulto => bulto.RecibidoEnDeposito is null).Select(bulto => bulto.Codigo).OrderBy(c => c).ToList()));
                continue;
            }

            var esperados = Math.Max(1, declarados.Count);
            try
            {
                await EjecutarAsync(operadorId, envioId, esperados, usuario, observaciones, cancellationToken);
                recibidos.Add(new RecepcionEscaneada(envio.NumeroSeguimiento, esperados, esperados, []));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                errores.Add($"{envio.NumeroSeguimiento}: {ex.Message}");
            }
        }

        if (llegadas.Count > 0)
            await _recepciones.GuardarAsync(cancellationToken);

        return new ResultadoEscaneoRecepcion(recibidos, noReconocidos, errores, llegadas, enCurso);
    }

    /// <summary>
    /// El operario decide que el resto de los bultos no va a llegar: recibe el envío con lo escaneado y registra
    /// la discrepancia con los códigos que faltan. Si alguno llega después, se escanea y completa la recepción.
    /// </summary>
    public async Task<RecepcionEscaneada> CerrarConFaltantesAsync(
        Guid operadorId,
        Guid envioId,
        string usuario,
        string observaciones = "",
        CancellationToken cancellationToken = default)
    {
        var envio = await _envios.ObtenerPorOperadorAsync(envioId, operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El envío no existe para ese operador.");
        var declarados = await _bultos.ListarPorEnvioAsync(operadorId, envioId, cancellationToken);
        var esperados = Math.Max(1, declarados.Count);
        var faltantes = declarados.Where(bulto => bulto.RecibidoEnDeposito is null).Select(bulto => bulto.Codigo).OrderBy(c => c).ToList();
        var recibidos = declarados.Count == 0 ? esperados : declarados.Count - faltantes.Count;
        var nota = faltantes.Count == 0
            ? observaciones
            : string.IsNullOrWhiteSpace(observaciones)
                ? $"Faltan: {string.Join(", ", faltantes)}"
                : $"{observaciones.Trim()} (faltan: {string.Join(", ", faltantes)})";
        await EjecutarAsync(operadorId, envioId, recibidos, usuario, nota, cancellationToken, faltantes);
        return new RecepcionEscaneada(envio.NumeroSeguimiento, esperados, recibidos, faltantes);
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
    IReadOnlyList<string> Errores,
    IReadOnlyList<LlegadaPosterior>? LlegadasPosteriores = null,
    IReadOnlyList<RecepcionEnCurso>? EnCurso = null);

/// <summary>Envío que se está escaneando: llegaron algunos bultos y faltan otros (todavía sin discrepancia).</summary>
public sealed record RecepcionEnCurso(string NumeroSeguimiento, int Escaneados, int Esperados, IReadOnlyList<string> Faltantes);

/// <summary>Un bulto que había faltado y llegó después; Completa = ya llegaron todos los de ese envío.</summary>
public sealed record LlegadaPosterior(string NumeroSeguimiento, string Codigo, bool Completa);

public sealed record DiscrepanciaRecepcion(
    string NumeroSeguimiento,
    string Destinatario,
    int BultosEsperados,
    int BultosRecibidos,
    string Observaciones,
    string Usuario,
    DateTimeOffset Fecha,
    string BultosFaltantes = "",
    bool Completada = false);

public sealed record ResultadoRecepcion(
    Guid EnvioId,
    Guid RecepcionId,
    bool HayDiscrepancia,
    int BultosEsperados,
    int BultosRecibidos,
    Guid? EventId);
