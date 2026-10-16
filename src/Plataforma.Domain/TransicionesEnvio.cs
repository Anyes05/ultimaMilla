namespace Plataforma.Domain;

/// <summary>Transiciones del diagrama de estados de envío.</summary>
public static class TransicionesEnvio
{
    private static readonly Dictionary<EstadoEnvio, EstadoEnvio[]> Permitidas = new()
    {
        [EstadoEnvio.Admitido] =
        [
            EstadoEnvio.EnDeposito,
            EstadoEnvio.Extraviado
        ],

        [EstadoEnvio.EnDeposito] =
        [
            EstadoEnvio.AsignadoARuta,
            EstadoEnvio.Extraviado
        ],

        [EstadoEnvio.AsignadoARuta] =
        [
            EstadoEnvio.EnTransito,
            EstadoEnvio.EnDeposito
        ],

        [EstadoEnvio.EnTransito] =
        [
            EstadoEnvio.Entregado,
            EstadoEnvio.NoEntregado,
            EstadoEnvio.Extraviado
        ],

        [EstadoEnvio.NoEntregado] =
        [
            EstadoEnvio.Reprogramado,
            EstadoEnvio.EnDevolucion,
            EstadoEnvio.Extraviado
        ],

        [EstadoEnvio.Reprogramado] =
        [
            EstadoEnvio.AsignadoARuta
        ],

        [EstadoEnvio.EnDevolucion] =
        [
            EstadoEnvio.Devuelto
        ],

        [EstadoEnvio.Entregado] = [],
        [EstadoEnvio.Devuelto] = [],
        [EstadoEnvio.Extraviado] = []
    };

    public static bool EsValida(EstadoEnvio origen, EstadoEnvio destino) =>
        Permitidas.TryGetValue(origen, out var permitidos) && permitidos.Contains(destino);

    public static IReadOnlyList<EstadoEnvio> DestinosDesde(EstadoEnvio origen) =>
        Permitidas.TryGetValue(origen, out var permitidos)
            ? permitidos
            : Array.Empty<EstadoEnvio>();
}
