using gestao_chamados.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace gestao_chamados.Services;

/// <summary>
/// Envio de notificações em tempo real (SignalR). Centraliza o que antes era duplicado nos controllers
/// para que o monitor de SLA e as regras de automação usem exatamente o mesmo caminho.
/// Os "grupos" são ids de usuário, "Agents" ou "Admins".
/// </summary>
public class TicketNotifier
{
    private readonly IHubContext<NotificationHub> _hub;

    public TicketNotifier(IHubContext<NotificationHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyAsync(string group, string message) =>
        _hub.Clients.Group(group).SendAsync("ReceiveNotification", message);

    /// <summary>Notifica cada grupo/usuário uma única vez, sem avisar quem fez a ação.</summary>
    public async Task NotifyOthersAsync(string? exceptUserId, string message, params string?[] groups)
    {
        foreach (var group in groups.OfType<string>().Distinct().Where(g => g != exceptUserId))
        {
            await NotifyAsync(group, message);
        }
    }
}
