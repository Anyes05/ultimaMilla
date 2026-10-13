using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class SincronizarEntregas
{
    private readonly IRutaRepositorio _rutas;
    private readonly IEnvioRepositorio _envios;
    private readonly IOperadorRepositorio _operadores;
    private readonly ICacheLectura _cache;

    public SincronizarEntregas(
        IRutaRepositorio rutas,
        IEnvioRepositorio envios,
        IOperadorRepositorio operadores,
        ICacheLectura cache)
    {
        _rutas = rutas;
        _envios = envios;
        _operadores = operadores;
        _cache = cache;
    }

    public async Task<ResultadoSincronizacion> Ejecutar(
        Guid operadorId,
        Guid repartidorId,
        DateOnly fecha,
        IReadOnlyList<(Guid EnvioId, EstadoEnvio Estado)> eventos,
        CancellationToken cancellationToken = default)
    {
        var operador = await _operadores.ObtenerAsync(operadorId, cancellationToken)
            ?? throw new InvalidOperationException("El operador de la sesión no existe.");

        var rutas = await _rutas.ObtenerDeHoyAsync(operadorId, repartidorId, fecha, cancellationToken);
        var rutasId = rutas.Select(ruta => ruta.Id).ToArray();
        var aplicados = new List<EventoAplicado>();
        var rechazados = new List<EventoRechazado>();

        foreach (var evento in eventos)
        {
            var envio = await _envios.ObtenerEnRutaAsync(evento.EnvioId, operadorId, rutasId, cancellationToken);
            if (envio is null)
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, "El envío no está en tu ruta de hoy."));
                continue;
            }

            if (evento.Estado == EstadoEnvio.NoEntregado && envio.IntentosRealizados >= operador.MaximoIntentos)
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, $"Se alcanzó el máximo de {operador.MaximoIntentos} intentos."));
                continue;
            }

            if (evento.Estado == EstadoEnvio.Reprogramado && envio.IntentosRealizados >= operador.MaximoIntentos)
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, $"El operador admite como máximo {operador.MaximoIntentos} intentos."));
                continue;
            }

            if (!TransicionesEnvio.EsValida(envio.Estado, evento.Estado))
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, $"Transición inválida: {envio.Estado} -> {evento.Estado}"));
                continue;
            }

            if (evento.Estado == EstadoEnvio.NoEntregado)
                envio.RegistrarIntentoFallido(operador.MaximoIntentos);

            envio.CambiarEstado(evento.Estado);
            aplicados.Add(new EventoAplicado(envio.Id, envio.Estado, envio.IntentosRealizados));
        }

        if (aplicados.Count > 0)
        {
            await _envios.GuardarAsync(cancellationToken);
            await _cache.QuitarAsync(ObtenerRutasDeHoy.Clave(operadorId, repartidorId, fecha), cancellationToken);
        }

        return new ResultadoSincronizacion(aplicados, rechazados);
    }
}
