using gestao_chamados.Data;

namespace gestao_chamados.Models.ViewModels;

public class MetricsViewModel
{
    public int Days { get; set; }
    public MetricsReport Report { get; set; } = null!;
    public bool IsAdmin { get; set; }
    public bool UsesBusinessHours { get; set; }
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
}
