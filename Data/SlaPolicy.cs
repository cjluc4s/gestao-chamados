using gestao_chamados.Models;

namespace gestao_chamados.Data;

public static class SlaPolicy
{
    /// <summary>
    /// Status em que o relógio do SLA para: o chamado já foi atendido ou não precisa mais ser.
    /// Array (e não método) para poder ser usado também dentro de consultas do EF.
    /// </summary>
    public static readonly TicketStatus[] ClockStoppedStatuses =
        [TicketStatus.Resolved, TicketStatus.Closed, TicketStatus.Cancelled];

    public static TimeSpan GetDeadline(TicketPriority priority)
    {
        return priority switch
        {
            TicketPriority.Critical => TimeSpan.FromHours(2),
            TicketPriority.High => TimeSpan.FromHours(4),
            TicketPriority.Medium => TimeSpan.FromHours(12),
            _ => TimeSpan.FromHours(24)
        };
    }

    /// <summary>
    /// O prazo conta sempre a partir da abertura do chamado, inclusive quando a
    /// prioridade é alterada depois: subir para Crítica num chamado antigo pode
    /// deixá-lo imediatamente vencido, que é o que se quer sinalizar.
    /// </summary>
    public static DateTime GetDueAt(DateTime createdAtUtc, TicketPriority priority) =>
        createdAtUtc.Add(GetDeadline(priority));

    /// <summary>
    /// Novo limite após uma edição. Sem mudança de prioridade o limite atual é mantido.
    /// </summary>
    public static DateTime? RecalculateDueAt(DateTime createdAtUtc, DateTime? currentDueAtUtc, TicketPriority oldPriority, TicketPriority newPriority) =>
        oldPriority == newPriority ? currentDueAtUtc : GetDueAt(createdAtUtc, newPriority);

    public static bool IsBreached(DateTime? dueAtUtc, TicketStatus status, DateTime nowUtc) =>
        dueAtUtc.HasValue
        && dueAtUtc.Value < nowUtc
        && !ClockStoppedStatuses.Contains(status);
}
