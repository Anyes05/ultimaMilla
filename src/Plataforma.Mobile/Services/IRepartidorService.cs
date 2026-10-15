using Plataforma.Mobile.Models;

namespace Plataforma.Mobile.Services;

public interface IRepartidorService
{
    bool HaySesion { get; }
    bool ConexionSimulada { get; set; }
    bool IniciarSesion(string email, string clave);
    Ruta ObtenerRuta();
    void RegistrarEntrega(Guid envioId, string nombreReceptor, string observaciones);
    ResultadoSincronizacion Sincronizar();
    void CerrarSesion();
}
