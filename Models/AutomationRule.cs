using System.ComponentModel.DataAnnotations;

namespace gestao_chamados.Models;

/// <summary>Quando a regra é avaliada.</summary>
public enum AutomationTrigger
{
    TicketCreated = 1,
    StatusChanged = 2,
    SlaWarning = 3,
    SlaBreached = 4,
    /// <summary>Chamado parado há N horas (sem comentário, mudança de status ou edição).</summary>
    Inactivity = 5
}

/// <summary>O que a regra faz quando as condições batem.</summary>
public enum AutomationActionType
{
    AssignToAgent = 1,
    SetPriority = 2,
    ChangeStatus = 3,
    NotifyAdmins = 4,
    /// <summary>O responsável pelo chamado ou, sem responsável, todos os atendentes.</summary>
    NotifyTeam = 5,
    NotifyRequester = 6
}

/// <summary>
/// Regra "quando [gatilho], se [condições], então [ação]". As condições são todas opcionais e
/// combinadas com E (todas precisam bater). Cada regra tem uma ação; para várias, crie várias regras.
/// </summary>
public class AutomationRule
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public AutomationTrigger Trigger { get; set; }

    // ---- Condições ----
    public int? ConditionCategoryId { get; set; }
    public Category? ConditionCategory { get; set; }

    public TicketPriority? ConditionPriority { get; set; }

    /// <summary>O chamado precisa estar neste status (no gatilho "Status alterado", é o novo status).</summary>
    public TicketStatus? ConditionStatus { get; set; }

    /// <summary>Palavras separadas por vírgula: basta uma aparecer no título ou na descrição.</summary>
    [StringLength(200)]
    public string? ConditionKeywords { get; set; }

    /// <summary>Só no gatilho "Inatividade": horas sem movimentação.</summary>
    public int? ConditionInactiveHours { get; set; }

    // ---- Ação ----
    public AutomationActionType ActionType { get; set; }

    public string? ActionAgentId { get; set; }
    public ApplicationUser? ActionAgent { get; set; }

    public TicketPriority? ActionPriority { get; set; }
    public TicketStatus? ActionStatus { get; set; }

    /// <summary>Texto da notificação. Aceita {id} e {titulo}.</summary>
    [StringLength(300)]
    public string? ActionMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<AutomationLog> Logs { get; set; } = new List<AutomationLog>();
}

/// <summary>Registro de cada vez que uma regra agiu em um chamado.</summary>
public class AutomationLog
{
    public int Id { get; set; }

    public int RuleId { get; set; }
    public AutomationRule? Rule { get; set; }

    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;

    [Required]
    [StringLength(300)]
    public string Description { get; set; } = string.Empty;
}
