namespace Plataforma.Domain;

/// <summary>
/// El estado del envío se deriva de los estados de sus bultos (RF 31).
/// </summary>
public static class DerivacionEstadoEnvio
{
    public static EstadoEnvio DesdeBultos(IReadOnlyList<EstadoBulto> estados)
    {
        if (estados is null || estados.Count == 0)
            throw new ArgumentException("Se necesita al menos un bulto.", nameof(estados));

        var entregados = estados.Count(e => e == EstadoBulto.Entregado);
        var enRuta = estados.Count(e => e == EstadoBulto.EnRuta);
        var pendientesVisita = estados.Count(e =>
            e is EstadoBulto.Pendiente or EstadoBulto.PendienteNuevaVisita or EstadoBulto.NoEntregado);
        var extraviados = estados.Count(e => e == EstadoBulto.Extraviado);
        var devueltos = estados.Count(e => e == EstadoBulto.Devuelto);

        if (extraviados == estados.Count)
            return EstadoEnvio.Extraviado;

        if (devueltos == estados.Count)
            return EstadoEnvio.Devuelto;

        if (entregados == estados.Count)
            return EstadoEnvio.Entregado;

        if (entregados > 0 && entregados < estados.Count)
            return EstadoEnvio.EntregadoParcialmente;

        if (enRuta > 0)
            return EstadoEnvio.EnTransito;

        if (pendientesVisita == estados.Count && estados.All(e => e == EstadoBulto.NoEntregado))
            return EstadoEnvio.NoEntregado;

        if (pendientesVisita > 0 && entregados == 0 && enRuta == 0)
            return EstadoEnvio.Reprogramado;

        return EstadoEnvio.EnDeposito;
    }

    /// <summary>True si el bulto puede asignarse a una nueva hoja de ruta (RF 34).</summary>
    public static bool DisponibleParaNuevaRuta(EstadoBulto estado) =>
        estado is EstadoBulto.Pendiente or EstadoBulto.PendienteNuevaVisita;
}
