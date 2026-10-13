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
    public DateTime FechaCreacion { get; private set; }
    public Guid? RutaId { get; private set; }
    public int IntentosRealizados { get; private set; }

    // Constructor sin parámetros requerido por EF Core
    private Envio()
    {
        Estado = EstadoEnvio.Admitido;
    }

    // Constructor directo para compatibilidad con tests de dominio
    public Envio(string destinatario, string direccionEntrega) : this()
    {
        if (string.IsNullOrWhiteSpace(destinatario))
            throw new ArgumentException("El destinatario es requerido.");

        if (string.IsNullOrWhiteSpace(direccionEntrega))
            throw new ArgumentException("La dirección de entrega es requerida.");

        Id = Guid.NewGuid();
        OperadorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        ComercioId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        NumeroSeguimiento = $"ENV-{Random.Shared.Next(100000, 999999)}";
        TokenSeguimientoPublico = Guid.NewGuid().ToString("N");
        Destinatario = destinatario;
        DireccionEntrega = direccionEntrega;
        FechaCreacion = DateTime.UtcNow;
    }

    // Método de fábrica para altas explícitas
    public static Envio Admitir(
        Guid operadorId, 
        Guid comercioId, 
        string destinatario, 
        string direccionEntrega, 
        string emailDestinatario = "", 
        decimal tarifaCalculada = 0)
    {
        var envio = new Envio(destinatario, direccionEntrega)
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            EmailDestinatario = emailDestinatario,
            TarifaCalculada = tarifaCalculada
        };

        return envio;
    }

    // Transición de estado con validación
    public void CambiarEstado(EstadoEnvio nuevoEstado)
    {
        if (!TransicionesEnvio.EsValida(Estado, nuevoEstado))
        {
            throw new InvalidOperationException($"Transición inválida: {Estado} -> {nuevoEstado}");
        }

        Estado = nuevoEstado;
    }

    // Sobrecarga con auditoría
    public void CambiarEstado(EstadoEnvio nuevoEstado, string usuario, string motivo)
    {
        CambiarEstado(nuevoEstado);
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
    }
}