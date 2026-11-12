using Plataforma.Mobile.Models;

namespace Plataforma.Mobile.Services;

public sealed class SesionRepartidorVencidaException() : InvalidOperationException(
    "Tu sesion ya no es valida. Ingresa de nuevo con la misma cuenta. Tus comprobantes siguen guardados.");

public interface IRepartidorService
{
    bool RequiereAutenticacion => false;
    IReadOnlyList<RetiroEnRuta> ObtenerRetiros() => [];
    void RegistrarRetiro(RetiroPendiente comprobante) => throw new InvalidOperationException("Retiros no disponibles en este servicio.");
    void RegistrarEvento(EntregaPendiente evento) => throw new InvalidOperationException("Operacion no disponible.");
    void RegistrarEscaneoBulto(Guid envioId, Guid bultoId, string codigo) => throw new InvalidOperationException("El escaneo de bultos no esta disponible.");
    void ReintentarRechazados() => throw new InvalidOperationException("Operacion no disponible.");
    Task RegistrarDispositivoAsync(string token, string plataforma) => throw new InvalidOperationException("Solo disponible en API.");
    Task BorrarDispositivoAsync(string token) => throw new InvalidOperationException("Solo disponible en API.");
    bool ModoDemo { get => true; set { if (!value) throw new InvalidOperationException("Este servicio solo admite demo."); } }
    Task<bool> IniciarSesionAsync(string usuario, string clave) => Task.FromResult(IniciarSesion(usuario, clave));
    Task<ResultadoSincronizacion> SincronizarAsync() => Task.FromResult(Sincronizar());
    Task DescargarRutaAsync() => Task.CompletedTask;
    Task IniciarRutaAsync() => throw new InvalidOperationException("Inicio de ruta no disponible en este servicio.");
    IReadOnlyList<EntregaPendiente> ObtenerHistorialEntregas() => ObtenerRuta().Envios
        .SelectMany(envio => (envio.Historial ?? [])
            .Where(comprobante => comprobante.Estado is EstadoEnvio.Entregado or EstadoEnvio.EntregadoParcialmente)
            .Select(comprobante => comprobante with
            {
                NumeroSeguimiento = comprobante.NumeroSeguimiento ?? envio.NumeroSeguimiento,
                Destinatario = comprobante.Destinatario ?? envio.Destinatario
            }))
        .OrderByDescending(comprobante => comprobante.Fecha)
        .ToArray();
    int ObtenerCantidadComprobantesFueraDeRuta() => 0;
    int ObtenerCantidadSincronizacionesPendientes() => ObtenerRuta().Envios
        .Count(envio => envio.Sincronizacion == EstadoSincronizacion.Pendiente);
    bool HaySesion { get; }
    bool ConexionSimulada { get; set; }
    bool IniciarSesion(string usuario, string clave);
    Ruta ObtenerRuta();
    void RegistrarEntrega(Guid envioId, string nombreReceptor, string observaciones);
    ResultadoSincronizacion Sincronizar();
    void CerrarSesion();
    Task CerrarSesionAsync()
    {
        CerrarSesion();
        return Task.CompletedTask;
    }
}
