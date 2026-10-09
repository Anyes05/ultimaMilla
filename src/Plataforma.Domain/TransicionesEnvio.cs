namespace Plataforma.Domain;

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
            EstadoEnvio.EnDevolucion
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
    public static bool EsValida(
        EstadoEnvio origen,
        EstadoEnvio destino)
    {
        return Permitidas.TryGetValue(origen, out var permitidos)
               && permitidos.Contains(destino);
    }
}