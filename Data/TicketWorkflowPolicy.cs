using gestao_chamados.Models;

namespace gestao_chamados.Data;

/// <summary>
/// Transições de status permitidas. Um chamado nunca volta para "Aberto",
/// e "Fechado" é final — só um Admin pode reabri-lo.
/// </summary>
public static class TicketWorkflowPolicy
{
    private static readonly Dictionary<TicketStatus, TicketStatus[]> Transitions = new()
    {
        [TicketStatus.Open] = [TicketStatus.InProgress, TicketStatus.WaitingUser, TicketStatus.Resolved, TicketStatus.Closed],
        [TicketStatus.InProgress] = [TicketStatus.WaitingUser, TicketStatus.Resolved, TicketStatus.Closed],
        [TicketStatus.WaitingUser] = [TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed],
        [TicketStatus.Resolved] = [TicketStatus.InProgress, TicketStatus.Closed],
        [TicketStatus.Closed] = []
    };

    public static IReadOnlyList<TicketStatus> GetAllowedTransitions(TicketStatus from, bool isAdmin)
    {
        if (from == TicketStatus.Closed && isAdmin)
        {
            return [TicketStatus.InProgress];
        }

        return Transitions.TryGetValue(from, out var allowed) ? allowed : [];
    }

    public static bool CanTransition(TicketStatus from, TicketStatus to, bool isAdmin) =>
        Enum.IsDefined(to) && GetAllowedTransitions(from, isAdmin).Contains(to);
}
