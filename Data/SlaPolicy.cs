using gestao_chamados.Models;

namespace gestao_chamados.Data;

public static class SlaPolicy
{
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

    public static bool IsBreached(DateTime? dueAtUtc, TicketStatus status, DateTime nowUtc) =>
        dueAtUtc.HasValue
        && dueAtUtc.Value < nowUtc
        && status != TicketStatus.Resolved
        && status != TicketStatus.Closed;
}
