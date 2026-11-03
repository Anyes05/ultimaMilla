using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Admite un envío y calcula la tarifa con la regla vigente del operador.</summary>
public sealed class AdmitirEnvio
{
    private readonly IEnvioRepositorio _envios;
    private readonly IBultoRepositorio _bultos;
    private readonly CalcularTarifa _tarifa;

    public AdmitirEnvio(IEnvioRepositorio envios, IBultoRepositorio bultos, CalcularTarifa tarifa)
    {
        _envios = envios;
        _bultos = bultos;
        _tarifa = tarifa;
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
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        if (comercioId == Guid.Empty)
            throw new ArgumentException("El comercio es requerido.", nameof(comercioId));

        if (cantidadBultos < 1)
            throw new ArgumentOutOfRangeException(nameof(cantidadBultos), "El envío debe tener al menos un bulto.");

        var tarifa = await _tarifa.EjecutarConReferenciaAsync(
            operadorId, pesoKg, distanciaKm, fragil, urgente, cancellationToken);

        var envio = Envio.Admitir(
            operadorId,
            comercioId,
            destinatario,
            direccionEntrega,
            emailDestinatario,
            tarifa.Importe,
            tarifa.ReglaTarifariaId,
            tarifa.VigenciaTarifaId);

        await _envios.AgregarAsync(envio);

        // Reparte el peso del envío entre bultos (capacidad de vehículo / RF 8).
        // El último se ajusta para que la suma coincida con pesoKg pese al redondeo.
        var pesos = RepartirPesoEntreBultos(pesoKg, cantidadBultos);
        for (var i = 1; i <= cantidadBultos; i++)
        {
            var codigo = $"{envio.NumeroSeguimiento}-B{i:00}";
            await _bultos.AgregarAsync(
                Bulto.Crear(Guid.NewGuid(), operadorId, envio.Id, codigo, pesos[i - 1]),
                cancellationToken);
        }

        return envio;
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
