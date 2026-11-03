using System;

namespace Plataforma.Domain;

public class Envio
{
    public Guid Id { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }

    public string NumeroSeguimiento { get; private set; } = string.Empty;
    public string TokenSeguimientoPublico { get; private set; } = string.Empty;

    public string Destinatario { get; private set; } = string.Empty;
    public string DireccionEntrega { get; private set; } = string.Empty;
    public string EmailDestinatario { get; private set; } = string.Empty;

    public EstadoEnvio Estado { get; private set; }
    public decimal TarifaCalculada { get; private set; }
    /// <summary>Regla tarifaria vigente al admitir (6.6: conserva el tratamiento original).</summary>
    public Guid? ReglaTarifariaId { get; private set; }
    /// <summary>Vigencia concreta aplicada al calcular la tarifa, si existía.</summary>
    public Guid? VigenciaTarifaId { get; private set; }
    /// <summary>Token de concurrencia optimista (6.4); se incrementa en cada mutación de estado.</summary>
    public int Version { get; private set; }
    public DateTime FechaCreacion { get; private set; }
    public Guid? RutaId { get; private set; }
    public int IntentosRealizados { get; private set; }

    // Constructor sin parámetros requerido por EF Core
    private Envio()
    {
        Estado = EstadoEnvio.Admitido;
    }

    /// <summary>
    /// Solo para tests de dominio. Producción debe usar <see cref="Admitir"/>.
    /// </summary>
    internal Envio(string destinatario, string direccionEntrega) : this()
    {
        if (string.IsNullOrWhiteSpace(destinatario))
            throw new ArgumentException("El destinatario es requerido.");

        if (string.IsNullOrWhiteSpace(direccionEntrega))
            throw new ArgumentException("La dirección de entrega es requerida.");

        Id = Guid.NewGuid();
        OperadorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        ComercioId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        NumeroSeguimiento = GenerarNumeroSeguimiento();
        TokenSeguimientoPublico = Guid.NewGuid().ToString("N");
        Destinatario = destinatario;
        DireccionEntrega = direccionEntrega;
        FechaCreacion = DateTime.UtcNow;
    }

    /// <summary>Número opaco y único (no secuencial, no adivinable).</summary>
    internal static string GenerarNumeroSeguimiento() =>
        $"ENV-{Guid.NewGuid():N}".ToUpperInvariant();

    public static Envio Admitir(
        Guid operadorId,
        Guid comercioId,
        string destinatario,
        string direccionEntrega,
        string emailDestinatario = "",
        decimal tarifaCalculada = 0,
        Guid? reglaTarifariaId = null,
        Guid? vigenciaTarifaId = null)
    {
        var envio = new Envio(destinatario, direccionEntrega)
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            EmailDestinatario = emailDestinatario,
            TarifaCalculada = tarifaCalculada,
            ReglaTarifariaId = reglaTarifariaId,
            VigenciaTarifaId = vigenciaTarifaId
        };

        return envio;
    }

    public void CambiarEstado(EstadoEnvio nuevoEstado)
    {
        if (!TransicionesEnvio.EsValida(Estado, nuevoEstado))
        {
            throw new InvalidOperationException($"Transición inválida: {Estado} -> {nuevoEstado}");
        }

        Estado = nuevoEstado;
        Version++;

        if (nuevoEstado is EstadoEnvio.EnDeposito or EstadoEnvio.Reprogramado)
            RutaId = null;
    }

    public void CambiarEstado(EstadoEnvio nuevoEstado, string usuario, string motivo)
    {
        _ = usuario;
        _ = motivo;
        CambiarEstado(nuevoEstado);
    }

    /// <summary>
    /// Aplica el estado derivado de los bultos respetando las transiciones del envío.
    /// </summary>
    public void AplicarEstadoDerivadoDeBultos(IReadOnlyList<EstadoBulto> estadosBultos)
    {
        var derivado = DerivacionEstadoEnvio.DesdeBultos(estadosBultos);
        if (derivado == Estado)
            return;

        if (!TransicionesEnvio.EsValida(Estado, derivado))
            throw new InvalidOperationException(
                $"El estado derivado {derivado} no es alcanzable desde {Estado}.");

        CambiarEstado(derivado);
    }

    public void AsignarARuta(Guid rutaId)
    {
        if (rutaId == Guid.Empty)
            throw new ArgumentException("La ruta es requerida.");

        CambiarEstado(EstadoEnvio.AsignadoARuta);
        RutaId = rutaId;
    }

    public void RegistrarIntentoFallido(int maximoIntentos)
    {
        if (maximoIntentos < 1)
            throw new ArgumentOutOfRangeException(nameof(maximoIntentos), "El máximo de intentos debe ser al menos 1.");

        if (IntentosRealizados >= maximoIntentos)
            throw new InvalidOperationException($"Se alcanzó el máximo de {maximoIntentos} intentos.");

        IntentosRealizados++;
        Version++;
    }
}
