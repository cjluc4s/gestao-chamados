namespace gestao_chamados.Models.ViewModels;

public class TicketListItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public TicketStatus Status { get; set; }
    public TicketPriority Priority { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public bool IsSlaBreached { get; set; }
}
