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

        var pesoPorBulto = cantidadBultos == 1 ? pesoKg : 0;
        for (var i = 1; i <= cantidadBultos; i++)
        {
            var codigo = $"{envio.NumeroSeguimiento}-B{i:00}";
            await _bultos.AgregarAsync(
                Bulto.Crear(Guid.NewGuid(), operadorId, envio.Id, codigo, pesoPorBulto),
                cancellationToken);
        }

        return envio;
    }
}
