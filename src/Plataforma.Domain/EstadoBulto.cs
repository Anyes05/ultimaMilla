namespace Plataforma.Domain;

/// <summary>Estado individual de un bulto (cambio RF 31–36).</summary>
public enum EstadoBulto
{
    Pendiente,
    EnRuta,
    Entregado,
    NoEntregado,
    PendienteNuevaVisita,
    Devuelto,
    Extraviado
}
