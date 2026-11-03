using Plataforma.Domain;

namespace Plataforma.PortalComercio;

/// <summary>Nombre visible y clase CSS de cada estado de envío (mismas clases status-* que el Backoffice).</summary>
public static class EstadosVista
{
    public static string Nombre(EstadoEnvio estado) => estado switch
    {
        EstadoEnvio.EnDeposito => "En depósito",
        EstadoEnvio.AsignadoARuta => "Asignado a ruta",
        EstadoEnvio.EnTransito => "En tránsito",
        EstadoEnvio.EntregadoParcialmente => "Entregado parcialmente",
        EstadoEnvio.NoEntregado => "No entregado",
        EstadoEnvio.EnDevolucion => "En devolución",
        _ => estado.ToString()
    };

    public static string Clase(EstadoEnvio estado) => estado switch
    {
        EstadoEnvio.EnDeposito => "status-en-deposito",
        EstadoEnvio.AsignadoARuta => "status-asignado-a-ruta",
        EstadoEnvio.EnTransito => "status-en-transito",
        EstadoEnvio.EntregadoParcialmente => "status-entregado-parcialmente",
        EstadoEnvio.NoEntregado => "status-no-entregado",
        EstadoEnvio.EnDevolucion => "status-en-devolucion",
        _ => "status-" + estado.ToString().ToLowerInvariant()
    };
}
