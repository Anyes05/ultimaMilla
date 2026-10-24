using Plataforma.Domain;

namespace Plataforma.Backoffice;

/// <summary>Nombre visible y clase CSS de cada estado de envío (mismas clases status-* de site.css).</summary>
public static class EstadosVista
{
    public static string Nombre(EstadoEnvio estado) => Nombre(estado.ToString());

    public static string Nombre(string estado) => estado switch
    {
        nameof(EstadoEnvio.EnDeposito) => "En depósito",
        nameof(EstadoEnvio.AsignadoARuta) => "Asignado a ruta",
        nameof(EstadoEnvio.EnTransito) => "En tránsito",
        nameof(EstadoEnvio.NoEntregado) => "No entregado",
        nameof(EstadoEnvio.EnDevolucion) => "En devolución",
        _ => estado
    };

    public static string Clase(EstadoEnvio estado) => Clase(estado.ToString());

    public static string Clase(string estado) => estado switch
    {
        nameof(EstadoEnvio.EnDeposito) => "status-en-deposito",
        nameof(EstadoEnvio.AsignadoARuta) => "status-asignado-a-ruta",
        nameof(EstadoEnvio.EnTransito) => "status-en-transito",
        nameof(EstadoEnvio.NoEntregado) => "status-no-entregado",
        nameof(EstadoEnvio.EnDevolucion) => "status-en-devolucion",
        _ => "status-" + estado.ToLowerInvariant()
    };

    public static string ColorMarca(Operador? operador) => operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };
}
