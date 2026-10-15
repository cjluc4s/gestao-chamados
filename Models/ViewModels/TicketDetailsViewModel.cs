using Microsoft.AspNetCore.Mvc.Rendering;

namespace gestao_chamados.Models.ViewModels;

public class TicketDetailsViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public TicketStatus Status { get; set; }
    public TicketPriority Priority { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public bool IsSlaBreached { get; set; }
    public List<TicketCommentViewModel> Comments { get; set; } = [];
    public List<TicketStatusHistoryViewModel> History { get; set; } = [];
    public List<AttachmentViewModel> Attachments { get; set; } = [];
    public List<TicketStatus> AllowedStatuses { get; set; } = [];
    public bool CanEdit { get; set; }
    public bool CanCancel { get; set; }
    public bool CanAssign { get; set; }
    public string? AssignedToUserId { get; set; }
    public List<SelectListItem> AssignableAgents { get; set; } = [];
}
