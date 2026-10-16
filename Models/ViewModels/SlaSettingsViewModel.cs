namespace gestao_chamados.Models.ViewModels;

public class SlaSettingsViewModel
{
    public List<SlaPriorityRow> Priorities { get; set; } = [];
    public bool BusinessHoursEnabled { get; set; }
    public string TimeZone { get; set; } = string.Empty;
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public List<DayOfWeek> Days { get; set; } = [];
    public List<DateOnly> Holidays { get; set; } = [];
    public int WarnAtPercent { get; set; }
    public int MonitorIntervalSeconds { get; set; }
}

public record SlaPriorityRow(TicketPriority Priority, TimeSpan Budget, bool UsesBusinessHours);
