namespace Plataforma.Domain;

/// <summary>
/// Qué hacer con bultos no entregados cuando el operador admite entrega parcial (RF 33).
/// </summary>
public enum PoliticaBultosPendientes
{
    NuevaVisitaSinRecargo = 0,
    NuevaVisitaConRecargo = 1,
    DevolucionAlComercio = 2
}
