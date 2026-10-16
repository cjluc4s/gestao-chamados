using System.ComponentModel.DataAnnotations;

namespace gestao_chamados.Models;

/// <summary>Avaliação do atendimento feita pelo solicitante (CSAT): uma por chamado.</summary>
public class TicketFeedback
{
    public int Id { get; set; }

    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    /// <summary>Nota de 1 (muito insatisfeito) a 5 (muito satisfeito).</summary>
    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(500)]
    public string? Comment { get; set; }

    [Required]
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
