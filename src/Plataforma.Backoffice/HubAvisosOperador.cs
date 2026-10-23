using Microsoft.AspNetCore.SignalR;
using Plataforma.Application;

namespace Plataforma.Backoffice;

public sealed class HubAvisosOperador : Hub
{
    public static string Grupo(Guid operadorId) => $"operador:{operadorId:N}";

    public override async Task OnConnectedAsync()
    {
        if (!AccesoActual.Intentar(Context.User!, out var acceso))
            throw new HubException("Sesión inválida.");

        await Groups.AddToGroupAsync(Context.ConnectionId, Grupo(acceso.OperadorId));
        await base.OnConnectedAsync();
    }
}