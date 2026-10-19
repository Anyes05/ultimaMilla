using Plataforma.Mobile.Models;

namespace Plataforma.Mobile.Services;

public sealed class RepartidorSeleccionService(
    RepartidorDemoService demo, RepartidorHttpService api) : IRepartidorService
{
    public bool ModoDemo { get; set; }
    private IRepartidorService Actual => ModoDemo ? demo : api;
    public bool HaySesion => Actual.HaySesion;
    public bool ConexionSimulada { get => Actual.ConexionSimulada; set => Actual.ConexionSimulada = value; }
    public bool IniciarSesion(string usuario, string clave) => Actual.IniciarSesion(usuario, clave);
    public Task<bool> IniciarSesionAsync(string usuario, string clave) => Actual.IniciarSesionAsync(usuario, clave);
    public Ruta ObtenerRuta() => Actual.ObtenerRuta();
    public void RegistrarEntrega(Guid envioId, string nombreReceptor, string observaciones) =>
        Actual.RegistrarEntrega(envioId, nombreReceptor, observaciones);
    public ResultadoSincronizacion Sincronizar() => Actual.Sincronizar();
    public Task<ResultadoSincronizacion> SincronizarAsync() => Actual.SincronizarAsync();
    public Task DescargarRutaAsync() => Actual.DescargarRutaAsync();
    public void CerrarSesion() => Actual.CerrarSesion();
}
