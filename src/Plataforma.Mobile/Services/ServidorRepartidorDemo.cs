using Plataforma.Mobile.Models;

namespace Plataforma.Mobile.Services;

// Doble en proceso: intercambia JSON del contrato aceptado; no es el backend real.
public static class ServidorRepartidorDemo
{
    public static string? Login(string solicitudJson)
    {
        var solicitud = ContratosRepartidor.Leer<LoginSolicitud>(solicitudJson);
        var credenciales = ContratosRepartidor.Leer<LoginSolicitud>(
            ContratosRepartidor.LeerEjemplo("login.request.json"));
        if (string.IsNullOrWhiteSpace(solicitud.Usuario) || string.IsNullOrWhiteSpace(solicitud.Clave))
            return null;
        if (!string.Equals(solicitud.Usuario.Trim(), credenciales.Usuario, StringComparison.OrdinalIgnoreCase)
            || solicitud.Clave != credenciales.Clave)
            return null;
        return ContratosRepartidor.LeerEjemplo("login.response.json");
    }

    public static string DescargarRutas() => ContratosRepartidor.LeerEjemplo("rutas-hoy.response.json");

    public static string Sincronizar(
        string solicitudJson, Ruta ruta, IDictionary<Guid, EstadoEnvio> estadosServidor)
    {
        var solicitud = ContratosRepartidor.Leer<SincronizacionSolicitud>(solicitudJson);
        if (solicitud.Fecha != ruta.Fecha || solicitud.Eventos is null
            || solicitud.Eventos.Any(evento => evento is null || evento.EventoId == Guid.Empty
                || evento.Estado is not (EstadoEnvio.Entregado or EstadoEnvio.EnTransito or EstadoEnvio.NoEntregado)
                || evento.Estado == EstadoEnvio.Entregado && string.IsNullOrWhiteSpace(evento.NombreReceptor)
                || evento.Estado == EstadoEnvio.NoEntregado && string.IsNullOrWhiteSpace(evento.Observaciones)
                || evento.NombreReceptor.Length > 100 || evento.Observaciones is null
                || evento.Observaciones.Length > 500)
            || solicitud.Eventos.Select(evento => evento.EnvioId).Distinct().Count() != solicitud.Eventos.Count
            || solicitud.Eventos.Select(evento => evento.EventoId).Distinct().Count() != solicitud.Eventos.Count)
            throw new InvalidOperationException("El lote JSON de sincronizacion tiene datos invalidos.");

        var aplicados = new List<AplicadoContrato>();
        var rechazados = new List<RechazadoContrato>();
        foreach (var evento in solicitud.Eventos)
        {
            if (!ruta.Envios.Any(envio => envio.Id == evento.EnvioId)
                || !estadosServidor.TryGetValue(evento.EnvioId, out var estado))
                throw new InvalidOperationException("El envio del lote JSON no pertenece a la ruta descargada.");

            var envio = ruta.Envios.Single(e => e.Id == evento.EnvioId);
            if (evento.Estado == EstadoEnvio.EnTransito
                    ? estado != EstadoEnvio.AsignadoARuta || evento.CodigoEscaneado != envio.NumeroSeguimiento
                    : estado != EstadoEnvio.EnTransito)
            {
                rechazados.Add(new RechazadoContrato(
                    evento.EnvioId, $"El servidor informa {estado}; no se sobrescribe.", estado));
                continue;
            }
            estadosServidor[evento.EnvioId] = evento.Estado;
            aplicados.Add(new AplicadoContrato(evento.EnvioId, evento.Estado,
                evento.Estado == EstadoEnvio.NoEntregado ? 1 : 0));
        }
        return ContratosRepartidor.Escribir(new SincronizacionRespuesta(aplicados, rechazados));
    }
}
