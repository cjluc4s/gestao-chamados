using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace gestao_chamados.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }

        if (Context.User?.IsInRole("Admin") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
        }

        if (Context.User?.IsInRole("Agent") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Agents");
        }

        await base.OnConnectedAsync();
    }
}
