using gestao_chamados.Models;

namespace gestao_chamados.Data;

/// <summary>
/// Efeitos de um chamado mudar de status ou receber uma resposta. Concentra tudo num lugar só
/// (SLA, data de resolução, primeira resposta) para que telas, monitor e automações não esqueçam nada.
/// </summary>
public static class TicketLifecycle
{
    /// <summary>
    /// Chamar sempre que o status muda (logo depois de atribuir o novo status ao chamado).
    /// Para/retoma o relógio do SLA e mantém a data de resolução.
    /// </summary>
    public static void OnStatusChanged(SlaCalculator sla, Ticket ticket, TicketStatus from, TicketStatus to, DateTime nowUtc)
    {
        sla.OnStatusChanged(ticket, from, to, nowUtc);

        var wasResolved = from is TicketStatus.Resolved or TicketStatus.Closed;
        var isResolved = to is TicketStatus.Resolved or TicketStatus.Closed;

        if (isResolved && ticket.ResolvedAtUtc is null)
        {
            ticket.ResolvedAtUtc = nowUtc;
        }
        else if (wasResolved && !isResolved)
        {
            ticket.ResolvedAtUtc = null; // reaberto: volta a contar
        }
    }

    /// <summary>
    /// Registra a primeira resposta da equipe: comentário de um atendente/admin que não é o próprio solicitante.
    /// </summary>
    public static void OnComment(Ticket ticket, string authorUserId, bool authorIsStaff, DateTime nowUtc)
    {
        if (ticket.FirstResponseAtUtc is null && authorIsStaff && authorUserId != ticket.CreatedByUserId)
        {
            ticket.FirstResponseAtUtc = nowUtc;
        }
    }
}
