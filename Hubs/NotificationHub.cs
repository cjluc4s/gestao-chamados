using gestao_chamados.Data;
using gestao_chamados.Models;
using gestao_chamados.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    private readonly ApplicationDbContext _db;
    private readonly PresenceTracker _presence;

    public NotificationHub(ApplicationDbContext db, PresenceTracker presence)
    {
        _db = db;
        _presence = presence;
    }

    public static string TicketGroup(int ticketId) => $"ticket-{ticketId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }

        if (Context.User?.IsInRole(RoleNames.Admin) == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
        }

        if (Context.User?.IsInRole(RoleNames.Agent) == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Agents");
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var (ticketId, viewers) in _presence.LeaveAll(Context.ConnectionId))
        {
            await Clients.Group(TicketGroup(ticketId)).SendAsync("PresenceChanged", ticketId, viewers);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>A tela de detalhes avisa que está aberta: entra no grupo do chamado e na lista de presença.</summary>
    public async Task ViewTicket(int ticketId)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId) || Context.User is null)
        {
            throw new HubException("Sessão inválida.");
        }

        var ticket = await _db.Tickets
            .AsNoTracking()
            .Where(t => t.Id == ticketId)
            .Select(t => new { t.CreatedByUserId, t.AssignedToUserId })
            .FirstOrDefaultAsync();

        // Mesma regra de acesso das telas e da API.
        if (ticket is null || !TicketAccessPolicy.CanAccess(
                TicketAccessPolicy.ResolveRole(Context.User), userId, ticket.CreatedByUserId, ticket.AssignedToUserId))
        {
            throw new HubException("Sem acesso a este chamado.");
        }

        var name = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync() ?? "Alguém";

        await Groups.AddToGroupAsync(Context.ConnectionId, TicketGroup(ticketId));
        var viewers = _presence.Join(ticketId, Context.ConnectionId, new PresenceViewer(userId, name));
        await Clients.Group(TicketGroup(ticketId)).SendAsync("PresenceChanged", ticketId, viewers);
    }

    public async Task LeaveTicket(int ticketId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketGroup(ticketId));
        var viewers = _presence.Leave(ticketId, Context.ConnectionId);
        await Clients.Group(TicketGroup(ticketId)).SendAsync("PresenceChanged", ticketId, viewers);
    }

    /// <summary>"Fulano está digitando…" para os outros que estão no mesmo chamado.</summary>
    public async Task Typing(int ticketId)
    {
        if (_presence.TryGetViewer(ticketId, Context.ConnectionId, out var viewer))
        {
            await Clients.OthersInGroup(TicketGroup(ticketId)).SendAsync("UserTyping", ticketId, viewer.Name);
        }
    }
}
