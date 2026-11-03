namespace Plataforma.Domain;

public enum EstadoEnvio
{
    Admitido,
    EnDeposito,
    AsignadoARuta,
    EnTransito,
    Entregado,
    NoEntregado,
    Reprogramado,
    EnDevolucion,
    Devuelto,
    Extraviado,
    /// <summary>Al final a propósito: no desplaza los ints históricos (0–9) en la base.</summary>
    EntregadoParcialmente
}
