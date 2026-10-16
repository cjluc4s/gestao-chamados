using gestao_chamados.Data;

namespace gestao_chamados.Models.ViewModels;

public class TicketListItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public TicketStatus Status { get; set; }
    public TicketPriority Priority { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DueAtUtc { get; set; }

    /// <summary>O limite ficou para trás do "agora efetivo" (vale também para chamados já encerrados).</summary>
    public bool IsSlaBreached { get; set; }

    /// <summary>O alerta de "em risco" já foi disparado para este prazo.</summary>
    public bool IsSlaWarned { get; set; }

    /// <summary>O prazo estourou e o chamado foi escalonado.</summary>
    public bool IsSlaEscalated { get; set; }

    public SlaState SlaState =>
        SlaPolicy.IsDone(Status) ? SlaState.Done
        : Status == TicketStatus.WaitingUser ? SlaState.Paused
        : IsSlaBreached ? SlaState.Breached
        : IsSlaWarned ? SlaState.AtRisk
        : SlaState.Ok;

    /// <summary>Texto curto do SLA para lista, CSV e PDF.</summary>
    public string SlaLabel => SlaState switch
    {
        SlaState.Breached => "Vencido",
        SlaState.AtRisk => "Em risco",
        SlaState.Paused => IsSlaBreached ? "Pausado (vencido)" : "Pausado",
        SlaState.Done => IsSlaBreached ? "Fora do prazo" : "No prazo",
        _ => "No prazo"
    };
}
