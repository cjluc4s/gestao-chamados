using System.ComponentModel.DataAnnotations;

namespace gestao_chamados.Models;

public class Ticket
{
    public int Id { get; set; }

    [Required]
    [StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(3000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public TicketPriority Priority { get; set; }

    [Required]
    public TicketStatus Status { get; set; } = TicketStatus.Open;

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    [Required]
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }

    public string? AssignedToUserId { get; set; }
    public ApplicationUser? AssignedToUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? DueAtUtc { get; set; }

    /// <summary>Quando o relógio do SLA parou (aguardando usuário, resolvido, fechado, cancelado); null = correndo.</summary>
    public DateTime? SlaPausedAtUtc { get; set; }

    /// <summary>Total de tempo (útil, quando aplicável) em que o relógio já ficou parado; empurra o limite.</summary>
    public long SlaPausedSeconds { get; set; }

    /// <summary>Quando o alerta de "em risco" foi disparado (uma vez por vida do prazo).</summary>
    public DateTime? SlaWarnedAtUtc { get; set; }

    /// <summary>Quando o prazo estourou e o chamado foi escalonado ao Admin.</summary>
    public DateTime? SlaEscalatedAtUtc { get; set; }

    /// <summary>Quando a equipe respondeu pela primeira vez (primeiro comentário de quem não é o solicitante).</summary>
    public DateTime? FirstResponseAtUtc { get; set; }

    /// <summary>Quando o chamado foi resolvido (ou fechado direto); volta a null se for reaberto.</summary>
    public DateTime? ResolvedAtUtc { get; set; }

    [StringLength(260)]
    public string? AttachmentPath { get; set; }

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    public ICollection<TicketComment> Comments { get; set; } = new List<TicketComment>();
    public ICollection<TicketStatusHistory> StatusHistory { get; set; } = new List<TicketStatusHistory>();
    public TicketFeedback? Feedback { get; set; }
}
