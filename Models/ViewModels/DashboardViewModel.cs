namespace gestao_chamados.Models.ViewModels;

public class DashboardViewModel
{
    public int TotalTickets { get; set; }
    public int OpenTickets { get; set; }
    public int ResolvedTickets { get; set; }
    public int SlaBreachedTickets { get; set; }
    public int SlaAtRiskTickets { get; set; }
    public double? AverageRating { get; set; }
    public int RatingCount { get; set; }
    public Dictionary<TicketPriority, int> ByPriority { get; set; } = [];
}
