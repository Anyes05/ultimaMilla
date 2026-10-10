using Plataforma.Domain;
//lolo
namespace Plataforma.Application;

public interface IEnvioRepositorio
{
    Task AgregarAsync(Envio envio);
    Task<List<Envio>> ObtenerTodosAsync();
}