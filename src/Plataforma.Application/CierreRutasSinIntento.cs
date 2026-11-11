using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Registra un intento fallido al día siguiente para envíos que quedaron en tránsito sin sincronizar actividad.</summary>
public sealed class CierreRutasSinIntento
{
    public const string UsuarioSistema = "sistema:cierre-rutas";
    public const string MotivoNoEntregado = "No se entregó el día asignado";

    private readonly IOperadorRepositorio _operadores;
    private readonly IRutaRepositorio _rutas;
    private readonly IEnvioRepositorio _envios;
    private readonly IBultoRepositorio _bultos;
    private readonly SincronizarEntregas _sincronizar;

    public CierreRutasSinIntento(
        IOperadorRepositorio operadores,
        IRutaRepositorio rutas,
        IEnvioRepositorio envios,
        IBultoRepositorio bultos,
        SincronizarEntregas sincronizar)
    {
        _operadores = operadores;
        _rutas = rutas;
        _envios = envios;
        _bultos = bultos;
        _sincronizar = sincronizar;
    }

    public async Task<int> EjecutarAsync(int diasARevisar = 3, CancellationToken cancellationToken = default)
    {
        if (diasARevisar < 1)
            throw new ArgumentOutOfRangeException(nameof(diasARevisar));

        var ahora = DateTime.UtcNow;
        var aplicadosTotal = 0;
        foreach (var operadorId in await _operadores.ListarIdsAsync(cancellationToken))
        {
            var operador = await _operadores.ObtenerAsync(operadorId, cancellationToken);
            if (operador is null) continue;

            var diaActual = operador.DiaActual(ahora);
            for (var diasAtras = 1; diasAtras <= diasARevisar; diasAtras++)
            {
                var fechaRuta = diaActual.AddDays(-diasAtras);
                var rutas = await _rutas.ObtenerDelDiaAsync(operadorId, fechaRuta, cancellationToken);
                foreach (var ruta in rutas)
                {
                    var envios = await _envios.ObtenerPorRutaAsync(operadorId, ruta.Id, cancellationToken);
                    var eventos = new List<ComprobanteEntrega>();
                    foreach (var envio in envios.Where(item => item.Estado == EstadoEnvio.EnTransito))
                    {
                        var bultos = await _bultos.ListarPorEnvioAsync(operadorId, envio.Id, cancellationToken);
                        var resultados = bultos
                            .Where(bulto => bulto.Estado is EstadoBulto.Pendiente or EstadoBulto.EnRuta or EstadoBulto.PendienteNuevaVisita)
                            .Select(bulto => new ResultadoBultoSync(
                                bulto.Id,
                                EstadoBulto.NoEntregado,
                                MotivoNoEntregado))
                            .ToArray();

                        eventos.Add(new ComprobanteEntrega(
                            Guid.NewGuid(),
                            envio.Id,
                            EstadoEnvio.NoEntregado,
                            string.Empty,
                            MotivoNoEntregado,
                            ahora,
                            Bultos: resultados.Length > 0 ? resultados : null));
                    }

                    foreach (var lote in eventos.Chunk(100))
                    {
                        var resultado = await _sincronizar.Ejecutar(
                            operadorId,
                            ruta.RepartidorId,
                            ruta.Fecha,
                            lote,
                            UsuarioSistema,
                            cancellationToken);
                        aplicadosTotal += resultado.Aplicados.Count;
                    }
                }
            }
        }

        return aplicadosTotal;
    }
}