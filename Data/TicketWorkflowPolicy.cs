using gestao_chamados.Models;

namespace gestao_chamados.Data;

/// <summary>
/// Transições de status permitidas. Um chamado nunca volta para "Aberto",
/// e "Fechado"/"Cancelado" são finais — só um Admin pode reabri-los.
/// </summary>
public static class TicketWorkflowPolicy
{
    // "Cancelado" não aparece como destino: cancelar é uma ação própria (CanCancel),
    // não uma opção do "Alterar status" do atendimento.
    private static readonly Dictionary<TicketStatus, TicketStatus[]> Transitions = new()
    {
        [TicketStatus.Open] = [TicketStatus.InProgress, TicketStatus.WaitingUser, TicketStatus.Resolved, TicketStatus.Closed],
        [TicketStatus.InProgress] = [TicketStatus.WaitingUser, TicketStatus.Resolved, TicketStatus.Closed],
        [TicketStatus.WaitingUser] = [TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed],
        [TicketStatus.Resolved] = [TicketStatus.InProgress, TicketStatus.Closed],
        [TicketStatus.Closed] = [],
        [TicketStatus.Cancelled] = []
    };

    public static bool IsFinal(TicketStatus status) =>
        status is TicketStatus.Closed or TicketStatus.Cancelled;

    public static IReadOnlyList<TicketStatus> GetAllowedTransitions(TicketStatus from, bool isAdmin)
    {
        if (IsFinal(from) && isAdmin)
        {
            return [TicketStatus.InProgress];
        }

        return Transitions.TryGetValue(from, out var allowed) ? allowed : [];
    }

    public static bool CanTransition(TicketStatus from, TicketStatus to, bool isAdmin) =>
        Enum.IsDefined(to) && GetAllowedTransitions(from, isAdmin).Contains(to);

    /// <summary>
    /// O solicitante só edita enquanto ninguém começou a tratar o chamado (Aberto);
    /// o Admin edita a qualquer momento.
    /// </summary>
    public static bool CanEdit(TicketStatus status, bool isAdmin, bool isCreator) =>
        isAdmin || (isCreator && status == TicketStatus.Open);

    /// <summary>
    /// Exclusão lógica: o chamado vai para "Cancelado" e o histórico é preservado.
    /// O solicitante cancela enquanto está Aberto; o Admin, em qualquer status não final.
    /// </summary>
    public static bool CanCancel(TicketStatus status, bool isAdmin, bool isCreator) =>
        !IsFinal(status) && (isAdmin || (isCreator && status == TicketStatus.Open));
}
