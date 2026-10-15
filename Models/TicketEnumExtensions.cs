namespace gestao_chamados.Models;

/// <summary>
/// Rótulos em PT-BR e classes CSS dos enums de chamado, num único lugar
/// (antes duplicados em cada view, CSV e PDF).
/// </summary>
public static class TicketEnumExtensions
{
    public static string Label(this TicketStatus status) => status switch
    {
        TicketStatus.Open => "Aberto",
        TicketStatus.InProgress => "Em andamento",
        TicketStatus.WaitingUser => "Aguardando usuário",
        TicketStatus.Resolved => "Resolvido",
        TicketStatus.Closed => "Fechado",
        _ => status.ToString()
    };

    public static string CssClass(this TicketStatus status) => status switch
    {
        TicketStatus.InProgress => "inprogress",
        TicketStatus.WaitingUser => "waitinguser",
        TicketStatus.Resolved => "resolved",
        TicketStatus.Closed => "closed",
        _ => "open"
    };

    public static string Label(this TicketPriority priority) => priority switch
    {
        TicketPriority.Low => "Baixa",
        TicketPriority.Medium => "Média",
        TicketPriority.High => "Alta",
        TicketPriority.Critical => "Crítica",
        _ => priority.ToString()
    };

    public static string CssClass(this TicketPriority priority) => priority switch
    {
        TicketPriority.Medium => "medium",
        TicketPriority.High => "high",
        TicketPriority.Critical => "critical",
        _ => "low"
    };
}
