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

    public virtual Task NotifyAsync(string group, string message) =>
        _hub.Clients.Group(group).SendAsync("ReceiveNotification", message);

    /// <summary>Notifica cada grupo/usuário uma única vez, sem avisar quem fez a ação.</summary>
    public async Task NotifyOthersAsync(string? exceptUserId, string message, params string?[] groups)
    {
        foreach (var group in groups.OfType<string>().Distinct().Where(g => g != exceptUserId))
        {
            await NotifyAsync(group, message);
        }
    }

    /// <summary>
    /// Quem precisa saber que o chamado mudou: exatamente quem pode vê-lo (mesma regra de
    /// <c>TicketAccessPolicy</c>) — Admins, o solicitante, o responsável atual e todos os
    /// atendentes enquanto o chamado está sem responsável.
    /// Quando a mudança é de atribuição (<paramref name="assignmentChanged"/>), também avisa o responsável
    /// anterior e, se o chamado entrou ou saiu da fila, todos os atendentes (a fila deles muda).
    /// </summary>
    public static IReadOnlyList<string> AudienceFor(
        string createdByUserId, string? assignedToUserId, string? previousAssigneeId = null, bool assignmentChanged = false)
    {
        var groups = new List<string> { "Admins", createdByUserId };

        if (assignedToUserId is not null)
        {
            groups.Add(assignedToUserId);
        }
        else
        {
            groups.Add("Agents");
        }

        if (assignmentChanged)
        {
            if (previousAssigneeId is not null)
            {
                groups.Add(previousAssigneeId);
            }
            else
            {
                groups.Add("Agents"); // saiu da fila geral
            }
        }

        return groups.Distinct().ToList();
    }

    /// <summary>
    /// Avisa as telas abertas (detalhes, lista, dashboard) que um chamado mudou, para que se atualizem sozinhas.
    /// <paramref name="kind"/>: created, status, comment, assignment, edit, cancel, attachment, sla, feedback, automation.
    /// </summary>
    public virtual async Task TicketChangedAsync(
        int ticketId, string kind, string? byUserId, string createdByUserId, string? assignedToUserId,
        string? previousAssigneeId = null, bool assignmentChanged = false)
    {
        var payload = new { ticketId, kind, byUserId };

        foreach (var group in AudienceFor(createdByUserId, assignedToUserId, previousAssigneeId, assignmentChanged))
        {
            await _hub.Clients.Group(group).SendAsync("TicketChanged", payload);
        }
    }
}
