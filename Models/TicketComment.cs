using System.ComponentModel.DataAnnotations;

namespace gestao_chamados.Models;

public class TicketComment
{
    public int Id { get; set; }

    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    [Required]
    [StringLength(2000)]
    public string Message { get; set; } = string.Empty;

    [Required]
    public string AuthorUserId { get; set; } = string.Empty;
    public ApplicationUser? AuthorUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
