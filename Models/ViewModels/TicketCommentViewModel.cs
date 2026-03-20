namespace gestao_chamados.Models.ViewModels;

public class TicketCommentViewModel
{
    public string Author { get; set; } = string.Empty;
    public bool IsAgent { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
