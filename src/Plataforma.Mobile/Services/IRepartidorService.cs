using Plataforma.Mobile.Models;

namespace Plataforma.Mobile.Services;

public interface IRepartidorService
{
    void RegistrarEvento(EntregaPendiente evento) => throw new InvalidOperationException("Operacion no disponible.");
    void ReintentarRechazados() => throw new InvalidOperationException("Operacion no disponible.");
    Task RegistrarDispositivoAsync(string token, string plataforma) => throw new InvalidOperationException("Solo disponible en API.");
    Task BorrarDispositivoAsync(string token) => throw new InvalidOperationException("Solo disponible en API.");
    bool ModoDemo { get => true; set { if (!value) throw new InvalidOperationException("Este servicio solo admite demo."); } }
    Task<bool> IniciarSesionAsync(string usuario, string clave) => Task.FromResult(IniciarSesion(usuario, clave));
    Task<ResultadoSincronizacion> SincronizarAsync() => Task.FromResult(Sincronizar());
    Task DescargarRutaAsync() => Task.CompletedTask;
    bool HaySesion { get; }
    bool ConexionSimulada { get; set; }
    bool IniciarSesion(string usuario, string clave);
    Ruta ObtenerRuta();
    void RegistrarEntrega(Guid envioId, string nombreReceptor, string observaciones);
    ResultadoSincronizacion Sincronizar();
    void CerrarSesion();
}
