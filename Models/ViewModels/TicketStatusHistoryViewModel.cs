namespace gestao_chamados.Models.ViewModels;

public class TicketStatusHistoryViewModel
{
    public TicketStatus? FromStatus { get; set; }
    public TicketStatus ToStatus { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
}
