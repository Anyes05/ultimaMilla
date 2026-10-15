using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class SincronizarEntregas
{
    private readonly IRutaRepositorio _rutas;
    private readonly IEnvioRepositorio _envios;
    private readonly ConfiguracionOperador _operadores;
    private readonly ICacheLectura _cache;

    public SincronizarEntregas(
        IRutaRepositorio rutas,
        IEnvioRepositorio envios,
        ConfiguracionOperador operadores,
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
        IReadOnlyList<ComprobanteEntrega> eventos,
        CancellationToken cancellationToken = default)
    {
        if (eventos.Select(evento => evento.EventoId).Distinct().Count() != eventos.Count)
            throw new ArgumentException("Hay eventoId duplicados en el lote.", nameof(eventos));

        if (eventos.Select(evento => evento.EnvioId).Distinct().Count() != eventos.Count)
            throw new ArgumentException("Hay envíos duplicados en el lote.", nameof(eventos));

        var operador = await _operadores.ObtenerEntidadAsync(operadorId, cancellationToken);
        var rutas = await _rutas.ObtenerDeHoyAsync(operadorId, repartidorId, fecha, cancellationToken);
        var rutasId = rutas.Select(ruta => ruta.Id).ToArray();
        var aplicados = new List<EventoAplicado>();
        var rechazados = new List<EventoRechazado>();
        var enviosAplicados = new List<Envio>();

        foreach (var evento in eventos)
        {
            if (evento.EventoId == Guid.Empty || evento.EnvioId == Guid.Empty)
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, "El comprobante no tiene identificadores válidos."));
                continue;
            }

            if (evento.NombreReceptor.Length > 100 || evento.Observaciones.Length > 500)
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, "El comprobante supera los largos permitidos."));
                continue;
            }

            if (evento.Estado == EstadoEnvio.Entregado && string.IsNullOrWhiteSpace(evento.NombreReceptor))
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, "La entrega requiere el nombre del receptor."));
                continue;
            }

            var envio = await _envios.ObtenerEnRutaAsync(evento.EnvioId, operadorId, rutasId, cancellationToken);
            if (envio is null)
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, "El envío no está en tu ruta de hoy."));
                continue;
            }

            if (evento.Estado == EstadoEnvio.NoEntregado && envio.IntentosRealizados >= operador.MaximoIntentos)
            {
                rechazados.Add(new EventoRechazado(
                    evento.EnvioId,
                    $"Se alcanzó el máximo de {operador.MaximoIntentos} intentos.",
                    envio.Estado));
                continue;
            }

            if (evento.Estado == EstadoEnvio.Reprogramado && envio.IntentosRealizados >= operador.MaximoIntentos)
            {
                rechazados.Add(new EventoRechazado(
                    evento.EnvioId,
                    $"El operador admite como máximo {operador.MaximoIntentos} intentos.",
                    envio.Estado));
                continue;
            }

            if (!TransicionesEnvio.EsValida(envio.Estado, evento.Estado))
            {
                rechazados.Add(new EventoRechazado(
                    evento.EnvioId,
                    $"Transición inválida: {envio.Estado} -> {evento.Estado}",
                    envio.Estado));
                continue;
            }

            if (evento.Estado == EstadoEnvio.NoEntregado)
                envio.RegistrarIntentoFallido(operador.MaximoIntentos);

            envio.CambiarEstado(evento.Estado, evento.NombreReceptor, evento.Observaciones);
            aplicados.Add(new EventoAplicado(envio.Id, envio.Estado, envio.IntentosRealizados));
            enviosAplicados.Add(envio);
        }

        if (aplicados.Count > 0)
        {
            await _envios.GuardarAsync(cancellationToken);
            await _cache.QuitarAsync(ObtenerRutasDeHoy.Clave(operadorId, repartidorId, fecha), cancellationToken);
            foreach (var envio in enviosAplicados)
                await CacheSeguimiento.PublicarAsync(_cache, envio, cancellationToken);
        }

        return new ResultadoSincronizacion(aplicados, rechazados);
    }
}
