using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Admite un envío y calcula la tarifa con la regla vigente del operador.</summary>
public sealed class AdmitirEnvio
{
    private readonly IEnvioRepositorio _envios;
    private readonly CalcularTarifa _tarifa;

    public AdmitirEnvio(IEnvioRepositorio envios, CalcularTarifa tarifa)
    {
        _envios = envios;
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
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        if (comercioId == Guid.Empty)
            throw new ArgumentException("El comercio es requerido.", nameof(comercioId));

        var importe = await _tarifa.EjecutarAsync(
            operadorId, pesoKg, distanciaKm, fragil, urgente, cancellationToken);

        var envio = Envio.Admitir(
            operadorId,
            comercioId,
            destinatario,
            direccionEntrega,
            emailDestinatario,
            importe);

        await _envios.AgregarAsync(envio);
        return envio;
    }
}
