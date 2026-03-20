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
}
