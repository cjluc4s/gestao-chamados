using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace gestao_chamados.Models.ViewModels;

public class AutomationRuleForm
{
    public int Id { get; set; }

    [Display(Name = "Nome da regra")]
    [StringLength(100, ErrorMessage = "O nome pode ter no máximo {1} caracteres.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Regra ativa")]
    public bool IsEnabled { get; set; } = true;

    [Display(Name = "Quando")]
    public AutomationTrigger Trigger { get; set; } = AutomationTrigger.TicketCreated;

    [Display(Name = "Categoria")]
    public int? ConditionCategoryId { get; set; }

    [Display(Name = "Prioridade")]
    public TicketPriority? ConditionPriority { get; set; }

    [Display(Name = "Status")]
    public TicketStatus? ConditionStatus { get; set; }

    [Display(Name = "O texto cita")]
    [StringLength(200, ErrorMessage = "Use no máximo {1} caracteres nas palavras.")]
    public string? ConditionKeywords { get; set; }

    [Display(Name = "Parado há (horas)")]
    public int? ConditionInactiveHours { get; set; }

    [Display(Name = "Então")]
    public AutomationActionType ActionType { get; set; } = AutomationActionType.NotifyAdmins;

    [Display(Name = "Atendente")]
    public string? ActionAgentId { get; set; }

    [Display(Name = "Nova prioridade")]
    public TicketPriority? ActionPriority { get; set; }

    [Display(Name = "Novo status")]
    public TicketStatus? ActionStatus { get; set; }

    [Display(Name = "Mensagem")]
    [StringLength(300, ErrorMessage = "A mensagem pode ter no máximo {1} caracteres.")]
    public string? ActionMessage { get; set; }

    [ValidateNever]
    public List<SelectListItem> Categories { get; set; } = [];

    [ValidateNever]
    public List<SelectListItem> Agents { get; set; } = [];

    public static AutomationRuleForm FromRule(AutomationRule r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        IsEnabled = r.IsEnabled,
        Trigger = r.Trigger,
        ConditionCategoryId = r.ConditionCategoryId,
        ConditionPriority = r.ConditionPriority,
        ConditionStatus = r.ConditionStatus,
        ConditionKeywords = r.ConditionKeywords,
        ConditionInactiveHours = r.ConditionInactiveHours,
        ActionType = r.ActionType,
        ActionAgentId = r.ActionAgentId,
        ActionPriority = r.ActionPriority,
        ActionStatus = r.ActionStatus,
        ActionMessage = r.ActionMessage
    };

    public void ApplyTo(AutomationRule r)
    {
        r.Name = Name ?? string.Empty;
        r.IsEnabled = IsEnabled;
        r.Trigger = Trigger;
        r.ConditionCategoryId = ConditionCategoryId;
        r.ConditionPriority = ConditionPriority;
        r.ConditionStatus = ConditionStatus;
        r.ConditionKeywords = ConditionKeywords;
        r.ConditionInactiveHours = ConditionInactiveHours;
        r.ActionType = ActionType;
        r.ActionAgentId = ActionAgentId;
        r.ActionPriority = ActionPriority;
        r.ActionStatus = ActionStatus;
        r.ActionMessage = ActionMessage;
    }
}

public record AutomationRuleRow(
    int Id,
    string Name,
    bool IsEnabled,
    string Sentence,
    int Runs,
    DateTime? LastRunAtUtc);

public record AutomationLogRow(DateTime ExecutedAtUtc, int RuleId, string RuleName, int TicketId, string TicketTitle, string Description);

public record AutomationTemplate(string Key, string Title, string Description);

public class AutomationIndexViewModel
{
    public List<AutomationRuleRow> Rules { get; set; } = [];
    public List<AutomationLogRow> RecentLogs { get; set; } = [];
    public IReadOnlyList<AutomationTemplate> Templates { get; set; } = [];
}
