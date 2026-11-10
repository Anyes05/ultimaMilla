using System.Diagnostics;
using Plataforma.Application.Mensajes;
using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Admite un envío y calcula la tarifa con la regla vigente del operador.</summary>
/// <summary>Peso y medidas declarados por el comercio para un bulto (RF 8).</summary>
public sealed record BultoDeclarado(decimal PesoKg, decimal AltoCm = 0, decimal AnchoCm = 0, decimal LargoCm = 0);

public sealed class AdmitirEnvio
{
    public const int MaximoBultos = 20;

    private readonly IEnvioRepositorio _envios;
    private readonly IBultoRepositorio _bultos;
    private readonly CalcularTarifa _tarifa;
    private readonly IOutboxRepositorio _outbox;
    private readonly IComercioRepositorio _comercios;
    private readonly ICoberturaRepositorio _cobertura;

    public AdmitirEnvio(
        IEnvioRepositorio envios,
        IBultoRepositorio bultos,
        CalcularTarifa tarifa,
        IOutboxRepositorio outbox,
        IComercioRepositorio comercios,
        ICoberturaRepositorio cobertura)
    {
        _envios = envios;
        _bultos = bultos;
        _tarifa = tarifa;
        _outbox = outbox;
        _comercios = comercios;
        _cobertura = cobertura;
    }

    public async Task<Envio> EjecutarAsync(
        Guid operadorId,
        Guid comercioId,
        string destinatario,
        string direccionEntrega,
        string emailDestinatario = "",
        decimal pesoKg = 0,
        decimal distanciaKm = 0,
        bool fragil = false,
        bool urgente = false,
        int cantidadBultos = 1,
        Guid? zonaId = null,
        Guid? franjaId = null,
        decimal volumenCm3 = 0,
        string? claveExterna = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<BultoDeclarado>? bultosDeclarados = null)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        // Con el detalle de cada bulto (RF 8), la cantidad, el peso total y el volumen salen de ahí.
        if (bultosDeclarados is { Count: > 0 })
        {
            if (bultosDeclarados.Count > MaximoBultos)
                throw new ArgumentOutOfRangeException(nameof(bultosDeclarados), $"Un envío admite como máximo {MaximoBultos} bultos.");
            if (bultosDeclarados.Any(bulto => bulto.PesoKg <= 0))
                throw new ArgumentOutOfRangeException(nameof(bultosDeclarados), "Cada bulto debe pesar más de 0 kg.");
            if (bultosDeclarados.Any(bulto => bulto.AltoCm < 0 || bulto.AnchoCm < 0 || bulto.LargoCm < 0))
                throw new ArgumentOutOfRangeException(nameof(bultosDeclarados), "Las medidas de un bulto no pueden ser negativas.");

            cantidadBultos = bultosDeclarados.Count;
            pesoKg = bultosDeclarados.Sum(bulto => bulto.PesoKg);
            if (volumenCm3 <= 0)
                volumenCm3 = bultosDeclarados.Sum(bulto => bulto.AltoCm * bulto.AnchoCm * bulto.LargoCm);
        }

        if (comercioId == Guid.Empty)
            throw new ArgumentException("El comercio es requerido.", nameof(comercioId));

        if (cantidadBultos < 1)
            throw new ArgumentOutOfRangeException(nameof(cantidadBultos), "El envío debe tener al menos un bulto.");

        if (!await _comercios.EstaActivoAsync(comercioId, cancellationToken))
            throw new InvalidOperationException("El comercio está dado de baja y no puede crear envíos.");

        if (!string.IsNullOrWhiteSpace(claveExterna))
        {
            var existente = await _envios.ObtenerPorClaveExternaAsync(
                operadorId, comercioId, claveExterna, cancellationToken);
            if (existente is not null)
                return existente;
        }

        await ValidarCoberturaAsync(operadorId, zonaId, franjaId, cancellationToken);

        var tarifa = await _tarifa.EjecutarConReferenciaAsync(
            operadorId,
            pesoKg,
            distanciaKm,
            fragil,
            urgente,
            cancellationToken,
            volumenCm3,
            zonaId.HasValue);

        var envio = Envio.Admitir(
            operadorId,
            comercioId,
            destinatario,
            direccionEntrega,
            emailDestinatario,
            tarifa.Importe,
            tarifa.ReglaTarifariaId,
            tarifa.VigenciaTarifaId,
            zonaId,
            franjaId,
            claveExterna);

        var pesos = RepartirPesoEntreBultos(pesoKg, cantidadBultos);
        for (var i = 1; i <= cantidadBultos; i++)
        {
            var codigo = $"{envio.NumeroSeguimiento}-B{i:00}";
            var declarado = bultosDeclarados is { Count: > 0 } ? bultosDeclarados[i - 1] : null;
            await _bultos.AgregarAsync(
                declarado is null
                    ? Bulto.Crear(Guid.NewGuid(), operadorId, envio.Id, codigo, pesos[i - 1])
                    : Bulto.Crear(Guid.NewGuid(), operadorId, envio.Id, codigo,
                        declarado.PesoKg, declarado.AltoCm, declarado.AnchoCm, declarado.LargoCm),
                cancellationToken);
        }

        var admitido = EnvioAdmitido.Crear(operadorId, envio.Id);
        await _outbox.AgregarAsync(
            MensajeOutbox.CrearPendiente(
                admitido.EventId,
                operadorId,
                envio.Id,
                EnvioAdmitido.Tipo,
                EnvioAdmitido.Version,
                admitido.Serializar(),
                admitido.Fecha,
                Activity.Current?.Id),
            cancellationToken);

        await _envios.AgregarAsync(envio);
        return envio;
    }

    private async Task ValidarCoberturaAsync(
        Guid operadorId,
        Guid? zonaId,
        Guid? franjaId,
        CancellationToken cancellationToken)
    {
        if (zonaId is Guid z)
        {
            _ = await _cobertura.ObtenerZonaAsync(operadorId, z, cancellationToken)
                ?? throw new InvalidOperationException("La zona no existe para ese operador.");
        }

        if (franjaId is Guid f)
        {
            var franja = await _cobertura.ObtenerFranjaAsync(operadorId, f, cancellationToken)
                ?? throw new InvalidOperationException("La franja no existe para ese operador.");
            if (!franja.Activa)
                throw new InvalidOperationException("La franja no está activa.");
            if (franja.ZonaId is Guid zonaFranja && zonaId is Guid zonaEnvio && zonaFranja != zonaEnvio)
                throw new InvalidOperationException("La franja no pertenece a la zona indicada.");
            if (franja.ZonaId is Guid zonaObligatoria && zonaId is null)
                throw new InvalidOperationException("La franja requiere indicar su zona.");
        }
    }

    public static decimal[] RepartirPesoEntreBultos(decimal pesoKg, int cantidadBultos)
    {
        if (cantidadBultos < 1)
            throw new ArgumentOutOfRangeException(nameof(cantidadBultos));

        if (pesoKg < 0)
            throw new ArgumentOutOfRangeException(nameof(pesoKg));

        if (pesoKg == 0)
            throw new ArgumentOutOfRangeException(nameof(pesoKg), "El peso del envío debe ser mayor que 0.");

        var pesos = new decimal[cantidadBultos];
        var baseKg = decimal.Round(pesoKg / cantidadBultos, 3, MidpointRounding.AwayFromZero);
        decimal acumulado = 0;
        for (var i = 0; i < cantidadBultos - 1; i++)
        {
            pesos[i] = baseKg > 0 ? baseKg : 0.001m;
            acumulado += pesos[i];
        }

        pesos[^1] = decimal.Round(pesoKg - acumulado, 3, MidpointRounding.AwayFromZero);
        if (pesos[^1] <= 0)
            pesos[^1] = 0.001m;

        return pesos;
    }
}
