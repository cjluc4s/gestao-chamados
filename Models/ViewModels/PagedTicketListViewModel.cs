namespace gestao_chamados.Models.ViewModels;

public class PagedTicketListViewModel
{
    public List<TicketListItemViewModel> Items { get; set; } = [];
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public TicketStatus? Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
