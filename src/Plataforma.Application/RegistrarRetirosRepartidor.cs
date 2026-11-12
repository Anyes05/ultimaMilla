using System.Text.Json;
using Plataforma.Domain;

namespace Plataforma.Application;

public interface IComprobanteRetiroRepositorio
{
    Task<ComprobanteRetiro?> ObtenerPorEventoAsync(Guid operadorId, Guid eventoId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ComprobanteRetiro>> ListarPorRetirosAsync(Guid operadorId, IReadOnlyCollection<Guid> retiroIds, CancellationToken cancellationToken = default);
    void Agregar(ComprobanteRetiro comprobante);
    void Descartar(ComprobanteRetiro comprobante);
}

/// <summary>Un retiro como lo ve la app en la ruta del día (RF 39 y 40).</summary>
public sealed record RetiroDeRuta(
    Guid Id,
    string Direccion,
    string Contacto,
    string Motivo,
    string Observaciones,
    int CantidadBultos,
    decimal PesoKg,
    int? PosicionEnRuta,
    TimeOnly? FranjaDesde,
    TimeOnly? FranjaHasta,
    EstadoRetiro Estado);

/// <summary>Lo que manda la app al registrar un retiro sin conexión (RF 40).</summary>
public sealed record ComprobanteRetiroApp(
    Guid EventoId,
    Guid RetiroId,
    string Resultado,
    IReadOnlyList<string>? CodigosBultos,
    string? Foto,
    string? Firma,
    string? NombreEntrega,
    string? Motivo,
    DateTimeOffset FechaDispositivo,
    decimal? Latitud,
    decimal? Longitud);

public sealed record RetiroAplicado(Guid RetiroId, EstadoRetiro Estado);

public sealed record RetiroRechazado(Guid RetiroId, string Motivo, EstadoRetiro? Estado = null);

public sealed record ResultadoRetirosApp(IReadOnlyList<RetiroAplicado> Aplicados, IReadOnlyList<RetiroRechazado> Rechazados);

/// <summary>
/// RF 40: la app registra el retiro (bultos, foto y firma) o el no retiro, también sin conexión, y lo sincroniza
/// en lote. Misma política que las entregas (ADR 0013): cada evento se aplica o se rechaza con motivo, y el mismo
/// <c>eventoId</c> reenviado devuelve el mismo resultado sin duplicar.
/// </summary>
public sealed class RegistrarRetirosRepartidor
{
    public const int MaximoPorLote = 100;

    private readonly IRutaRepositorio _rutas;
    private readonly IRetiroRepositorio _retiros;
    private readonly IComprobanteRetiroRepositorio _comprobantes;
    private readonly GestionRetiros _gestion;
    private readonly ICacheLectura _cache;

    public RegistrarRetirosRepartidor(
        IRutaRepositorio rutas,
        IRetiroRepositorio retiros,
        IComprobanteRetiroRepositorio comprobantes,
        GestionRetiros gestion,
        ICacheLectura cache)
    {
        _rutas = rutas;
        _retiros = retiros;
        _comprobantes = comprobantes;
        _gestion = gestion;
        _cache = cache;
    }

    public async Task<ResultadoRetirosApp> EjecutarAsync(
        Guid operadorId,
        Guid repartidorId,
        DateOnly fecha,
        IReadOnlyList<ComprobanteRetiroApp> eventos,
        string usuario,
        CancellationToken cancellationToken = default)
    {
        if (eventos is null || eventos.Count == 0 || eventos.Count > MaximoPorLote)
            throw new ArgumentException($"El lote debe tener entre 1 y {MaximoPorLote} retiros.");
        if (eventos.Select(evento => evento.EventoId).Distinct().Count() != eventos.Count)
            throw new ArgumentException("Hay eventoId duplicados en el lote.");

        var rutasDelDia = (await _rutas.ObtenerDeHoyAsync(operadorId, repartidorId, fecha, cancellationToken))
            .Select(ruta => ruta.Id)
            .ToHashSet();
        var aplicados = new List<RetiroAplicado>();
        var rechazados = new List<RetiroRechazado>();

        foreach (var evento in eventos)
        {
            if (evento.EventoId == Guid.Empty || evento.RetiroId == Guid.Empty)
            {
                rechazados.Add(new RetiroRechazado(evento.RetiroId, "El comprobante no tiene identificadores válidos."));
                continue;
            }

            // Idempotencia: el mismo evento ya registrado devuelve el estado actual, sin volver a aplicarlo.
            if (await _comprobantes.ObtenerPorEventoAsync(operadorId, evento.EventoId, cancellationToken) is { } previo)
            {
                var actual = await _retiros.ObtenerAsync(operadorId, previo.RetiroId, cancellationToken);
                if (previo.RetiroId == evento.RetiroId && actual is not null)
                    aplicados.Add(new RetiroAplicado(actual.Id, actual.Estado));
                else
                    rechazados.Add(new RetiroRechazado(evento.RetiroId, "El eventoId ya fue utilizado por otro comprobante."));
                continue;
            }

            var retiro = await _retiros.ObtenerAsync(operadorId, evento.RetiroId, cancellationToken);
            if (retiro is null || retiro.RutaId is not Guid rutaId || !rutasDelDia.Contains(rutaId))
            {
                rechazados.Add(new RetiroRechazado(evento.RetiroId, "El retiro no está en tu ruta de hoy.", retiro?.Estado));
                continue;
            }

            EstadoRetiro resultado;
            if (string.Equals(evento.Resultado, "Retirado", StringComparison.OrdinalIgnoreCase))
                resultado = EstadoRetiro.Completado;
            else if (string.Equals(evento.Resultado, "NoRetirado", StringComparison.OrdinalIgnoreCase))
                resultado = EstadoRetiro.NoRetirado;
            else
            {
                rechazados.Add(new RetiroRechazado(retiro.Id, "El resultado tiene que ser Retirado o NoRetirado.", retiro.Estado));
                continue;
            }

            if (!TransicionesRetiro.EsValida(retiro.Estado, resultado))
            {
                rechazados.Add(new RetiroRechazado(retiro.Id, $"El retiro ya está {retiro.Estado}.", retiro.Estado));
                continue;
            }

            var errorEvidencia = EvidenciaRepartidor.Validar(evento.Foto, evento.Firma, evento.Latitud, evento.Longitud, evento.FechaDispositivo);
            if (errorEvidencia is not null)
            {
                rechazados.Add(new RetiroRechazado(retiro.Id, errorEvidencia, retiro.Estado));
                continue;
            }

            var codigos = (evento.CodigosBultos ?? [])
                .Where(codigo => !string.IsNullOrWhiteSpace(codigo))
                .Select(codigo => codigo.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (codigos.Any(codigo => codigo.Length > 100) || codigos.Count > 100)
            {
                rechazados.Add(new RetiroRechazado(retiro.Id, "Los códigos de los bultos son demasiado largos o demasiados.", retiro.Estado));
                continue;
            }

            ComprobanteRetiro comprobante;
            try
            {
                comprobante = ComprobanteRetiro.Registrar(
                    operadorId, retiro.Id, evento.EventoId, resultado, codigos, evento.Foto, evento.Firma,
                    evento.NombreEntrega, evento.Motivo, evento.FechaDispositivo, evento.Latitud, evento.Longitud,
                    usuario, DateTime.UtcNow);
            }
            catch (ArgumentException ex)
            {
                rechazados.Add(new RetiroRechazado(retiro.Id, TextoError(ex), retiro.Estado));
                continue;
            }

            // El comprobante se guarda en la misma transacción que el cambio de estado (y su evento del outbox).
            _comprobantes.Agregar(comprobante);
            try
            {
                if (resultado == EstadoRetiro.Completado)
                    await _gestion.CompletarAsync(operadorId, retiro.Id, cancellationToken: cancellationToken);
                else
                {
                    await _gestion.NoRetirarAsync(operadorId, retiro.Id, comprobante.Motivo, cancellationToken);
                }

                aplicados.Add(new RetiroAplicado(retiro.Id, resultado));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                _comprobantes.Descartar(comprobante);
                rechazados.Add(new RetiroRechazado(retiro.Id, TextoError(ex), retiro.Estado));
            }
        }

        if (aplicados.Count > 0)
            await _cache.QuitarAsync(ObtenerRutasDeHoy.Clave(operadorId, repartidorId, fecha), cancellationToken);

        return new ResultadoRetirosApp(aplicados, rechazados);
    }

    private static string TextoError(Exception ex) =>
        ex is ArgumentException argumento && argumento.ParamName is { } nombre
            ? ex.Message.Replace($" (Parameter '{nombre}')", string.Empty)
            : ex.Message;
}

/// <summary>Validación de la evidencia que manda la app: mismos límites que las entregas.</summary>
public static class EvidenciaRepartidor
{
    public static string? Validar(string? foto, string? firma, decimal? latitud, decimal? longitud, DateTimeOffset fechaDispositivo)
    {
        if (fechaDispositivo == default)
            return "La hora del dispositivo es requerida.";
        if (latitud.HasValue != longitud.HasValue || latitud is < -90 or > 90 || longitud is < -180 or > 180)
            return "La posición debe incluir latitud y longitud válidas.";
        if (!string.IsNullOrEmpty(firma))
        {
            if (firma.Length > 65_536)
                return "La firma supera 64 KiB.";
            try
            {
                using var documento = JsonDocument.Parse(firma);
                if (documento.RootElement.ValueKind != JsonValueKind.Array)
                    return "La firma debe ser un arreglo JSON de trazos.";
            }
            catch (JsonException)
            {
                return "La firma no contiene puntos JSON válidos.";
            }
        }

        if (!string.IsNullOrEmpty(foto))
        {
            if (foto.Length > 699_052)
                return "La foto supera 512 KiB.";
            try
            {
                var bytes = Convert.FromBase64String(foto);
                if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9)
                    return "La foto debe ser JPEG de hasta 512 KiB.";
            }
            catch (FormatException)
            {
                return "La foto no es base64 válido.";
            }
        }

        return null;
    }
}
