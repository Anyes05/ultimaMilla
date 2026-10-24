using Microsoft.AspNetCore.SignalR;
using Plataforma.Application;

namespace Plataforma.Backoffice;

/// <summary>
/// Canal en tiempo real del tablero (letra 6.10). El grupo sale del claim operador_id:
/// el cliente no elige operador, así Norte nunca recibe eventos de Sur.
/// Con varias réplicas, el backplane de Redis (Program.cs) reparte los mensajes entre instancias.
/// </summary>
public sealed class HubAvisosOperador : Hub
{
    public const string Ruta = "/hubs/avisos";

    public static string Grupo(Guid operadorId) => $"operador:{operadorId:N}";

    public override async Task OnConnectedAsync()
    {
        if (!AccesoActual.Intentar(Context.User!, out var acceso))
            throw new HubException("Sesión inválida.");

        await Groups.AddToGroupAsync(Context.ConnectionId, Grupo(acceso.OperadorId));
        await base.OnConnectedAsync();
    }
}
