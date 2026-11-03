namespace Plataforma.Domain;

/// <summary>Transiciones declarativas del bulto (verificables en tests).</summary>
public static class TransicionesBulto
{
    private static readonly Dictionary<EstadoBulto, EstadoBulto[]> Permitidas = new()
    {
        [EstadoBulto.Pendiente] =
        [
            EstadoBulto.EnRuta,
            EstadoBulto.Extraviado,
            EstadoBulto.Devuelto
        ],
        [EstadoBulto.EnRuta] =
        [
            EstadoBulto.Entregado,
            EstadoBulto.NoEntregado,
            EstadoBulto.Extraviado,
            EstadoBulto.PendienteNuevaVisita
        ],
        [EstadoBulto.NoEntregado] =
        [
            EstadoBulto.PendienteNuevaVisita,
            EstadoBulto.Devuelto,
            EstadoBulto.Extraviado
        ],
        [EstadoBulto.PendienteNuevaVisita] =
        [
            EstadoBulto.EnRuta,
            EstadoBulto.Devuelto,
            EstadoBulto.Extraviado
        ],
        [EstadoBulto.Entregado] = [],
        [EstadoBulto.Devuelto] = [],
        [EstadoBulto.Extraviado] = []
    };

    public static bool EsValida(EstadoBulto origen, EstadoBulto destino) =>
        Permitidas.TryGetValue(origen, out var permitidos) && permitidos.Contains(destino);

    public static IReadOnlyList<EstadoBulto> DestinosDesde(EstadoBulto origen) =>
        Permitidas.TryGetValue(origen, out var permitidos)
            ? permitidos
            : Array.Empty<EstadoBulto>();
}
