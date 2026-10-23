using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Lista las rutas del día del operador con repartidor y envíos (reemplaza el bucle de Despacho).</summary>
public sealed class ListarRutasDelDia
{
    private readonly IRutaRepositorio _rutas;
    private readonly IEnvioRepositorio _envios;
    private readonly IRepartidorRepositorio _repartidores;

    public ListarRutasDelDia(
        IRutaRepositorio rutas,
        IEnvioRepositorio envios,
        IRepartidorRepositorio repartidores)
    {
        _rutas = rutas;
        _envios = envios;
        _repartidores = repartidores;
    }

    public async Task<IReadOnlyList<RutaDelDiaResumen>> EjecutarAsync(
        Guid operadorId,
        DateOnly fecha,
        CancellationToken cancellationToken = default)
    {
        if (operadorId == Guid.Empty)
            throw new ArgumentException("El operador es requerido.", nameof(operadorId));

        var rutas = await _rutas.ObtenerDelDiaAsync(operadorId, fecha, cancellationToken);
        if (rutas.Count == 0)
            return [];

        var nombres = (await _repartidores.ListarActivosAsync(operadorId, cancellationToken))
            .ToDictionary(repartidor => repartidor.Id, repartidor => repartidor.Nombre);

        foreach (var id in rutas.Select(ruta => ruta.RepartidorId).Distinct().Where(id => !nombres.ContainsKey(id)))
        {
            var repartidor = await _repartidores.ObtenerAsync(id, operadorId, cancellationToken);
            if (repartidor is not null)
                nombres[id] = repartidor.Nombre;
        }

        var resultado = new List<RutaDelDiaResumen>(rutas.Count);
        foreach (var ruta in rutas)
        {
            var envios = await _envios.ObtenerPorRutaAsync(operadorId, ruta.Id, cancellationToken);
            var nombre = nombres.TryGetValue(ruta.RepartidorId, out var visible)
                ? visible
                : "Repartidor";
            resultado.Add(new RutaDelDiaResumen(ruta.Id, ruta.RepartidorId, nombre, envios));
        }

        return resultado;
    }
}

public sealed record RutaDelDiaResumen(
    Guid RutaId,
    Guid RepartidorId,
    string Repartidor,
    IReadOnlyList<Envio> Envios)
{
    public int PendientesDeSalir => Envios.Count(envio => envio.Estado == EstadoEnvio.AsignadoARuta);
}
