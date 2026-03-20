namespace gestao_chamados.Models;

public class TicketStatusHistory
{
    public int Id { get; set; }

    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public TicketStatus? FromStatus { get; set; }
    public TicketStatus ToStatus { get; set; }

    public string ChangedByUserId { get; set; } = string.Empty;
    public ApplicationUser? ChangedByUser { get; set; }

    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
