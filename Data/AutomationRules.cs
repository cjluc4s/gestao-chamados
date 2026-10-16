using System.Globalization;
using System.Text;
using gestao_chamados.Models;

namespace gestao_chamados.Data;

/// <summary>Usuário interno que assina as mudanças feitas por regras de automação. Não faz login.</summary>
public static class AutomationUser
{
    public const string Id = "system-automation";
    public const string UserName = "automacao@sistema.local";
    public const string FullName = "Automação";
}

/// <summary>Fotografia do chamado usada para avaliar condições (sem depender do banco).</summary>
public sealed record TicketSnapshot(
    int Id,
    string Title,
    string Description,
    int CategoryId,
    TicketPriority Priority,
    TicketStatus Status,
    DateTime LastActivityUtc,
    string? AssignedToUserId);

public static class RuleMatcher
{
    /// <summary>Todas as condições preenchidas precisam bater (E). Sem condições, a regra vale para qualquer chamado.</summary>
    public static bool Matches(AutomationRule rule, TicketSnapshot ticket, DateTime nowUtc)
    {
        if (!rule.IsEnabled)
        {
            return false;
        }

        if (rule.ConditionCategoryId is { } category && ticket.CategoryId != category)
        {
            return false;
        }

        if (rule.ConditionPriority is { } priority && ticket.Priority != priority)
        {
            return false;
        }

        if (rule.ConditionStatus is { } status && ticket.Status != status)
        {
            return false;
        }

        if (!MatchesKeywords(rule.ConditionKeywords, ticket.Title, ticket.Description))
        {
            return false;
        }

        if (rule.Trigger == AutomationTrigger.Inactivity)
        {
            var hours = rule.ConditionInactiveHours;
            if (hours is null or <= 0)
            {
                return false; // sem prazo definido, uma regra de inatividade nunca dispararia no momento certo
            }

            if (nowUtc - ticket.LastActivityUtc < TimeSpan.FromHours(hours.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Sem palavras = sem filtro. Ignora maiúsculas e acentos ("conexão" casa com "conexao").</summary>
    public static bool MatchesKeywords(string? keywords, string title, string description)
    {
        var words = SplitKeywords(keywords);
        if (words.Count == 0)
        {
            return true;
        }

        var text = Normalize(title + " " + description);
        return words.Any(w => text.Contains(Normalize(w), StringComparison.Ordinal));
    }

    public static List<string> SplitKeywords(string? keywords) =>
        (keywords ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 0)
            .ToList();

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}

public sealed record RuleError(string Field, string Message);

public static class AutomationRuleValidator
{
    public const int MaxInactiveHours = 24 * 365;

    public static List<RuleError> Validate(AutomationRule rule)
    {
        var errors = new List<RuleError>();

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            errors.Add(new("Name", "Dê um nome para a regra."));
        }

        if (!Enum.IsDefined(rule.Trigger))
        {
            errors.Add(new("Trigger", "Escolha quando a regra deve agir."));
        }

        if (rule.Trigger == AutomationTrigger.Inactivity)
        {
            if (rule.ConditionInactiveHours is not (>= 1 and <= MaxInactiveHours))
            {
                errors.Add(new("ConditionInactiveHours", $"Informe quantas horas parado (de 1 a {MaxInactiveHours})."));
            }
        }

        if (rule.ConditionPriority is { } cp && !Enum.IsDefined(cp))
        {
            errors.Add(new("ConditionPriority", "Prioridade inválida."));
        }

        if (rule.ConditionStatus is { } cs && !Enum.IsDefined(cs))
        {
            errors.Add(new("ConditionStatus", "Status inválido."));
        }

        if (!Enum.IsDefined(rule.ActionType))
        {
            errors.Add(new("ActionType", "Escolha o que a regra deve fazer."));
            return errors;
        }

        switch (rule.ActionType)
        {
            case AutomationActionType.AssignToAgent:
                if (string.IsNullOrWhiteSpace(rule.ActionAgentId))
                {
                    errors.Add(new("ActionAgentId", "Escolha o atendente."));
                }

                break;

            case AutomationActionType.SetPriority:
                if (rule.ActionPriority is not { } ap || !Enum.IsDefined(ap))
                {
                    errors.Add(new("ActionPriority", "Escolha a nova prioridade."));
                }
                else if (rule.ConditionPriority == ap)
                {
                    errors.Add(new("ActionPriority", "A nova prioridade é igual à da condição: a regra não mudaria nada."));
                }

                break;

            case AutomationActionType.ChangeStatus:
                if (rule.ActionStatus is not { } st || !Enum.IsDefined(st))
                {
                    errors.Add(new("ActionStatus", "Escolha o novo status."));
                }
                else if (st == TicketStatus.Open)
                {
                    errors.Add(new("ActionStatus", "Um chamado não volta para \"Aberto\"."));
                }
                else if (rule.ConditionStatus == st)
                {
                    errors.Add(new("ActionStatus", "O novo status é igual ao da condição: a regra não mudaria nada."));
                }

                break;
        }

        return errors;
    }

    /// <summary>Zera o que não se aplica ao gatilho e à ação escolhidos, para não guardar lixo.</summary>
    public static void Normalize(AutomationRule rule)
    {
        rule.Name = rule.Name.Trim();
        rule.ConditionKeywords = string.IsNullOrWhiteSpace(rule.ConditionKeywords)
            ? null
            : string.Join(", ", RuleMatcher.SplitKeywords(rule.ConditionKeywords));

        if (rule.Trigger != AutomationTrigger.Inactivity)
        {
            rule.ConditionInactiveHours = null;
        }

        if (rule.ActionType != AutomationActionType.AssignToAgent)
        {
            rule.ActionAgentId = null;
        }

        if (rule.ActionType != AutomationActionType.SetPriority)
        {
            rule.ActionPriority = null;
        }

        if (rule.ActionType != AutomationActionType.ChangeStatus)
        {
            rule.ActionStatus = null;
        }

        var notifies = rule.ActionType is AutomationActionType.NotifyAdmins or AutomationActionType.NotifyTeam or AutomationActionType.NotifyRequester;
        rule.ActionMessage = notifies && !string.IsNullOrWhiteSpace(rule.ActionMessage) ? rule.ActionMessage.Trim() : null;
    }
}

public static class AutomationPolicy
{
    /// <summary>
    /// A automação segue o fluxo normal de status (nunca volta para "Aberto", nada sai de Fechado/Cancelado)
    /// e, diferente do atendente, também pode cancelar um chamado que ainda não terminou.
    /// </summary>
    public static bool CanApplyStatus(TicketStatus from, TicketStatus to)
    {
        if (!Enum.IsDefined(to) || from == to)
        {
            return false;
        }

        if (to == TicketStatus.Cancelled)
        {
            return !TicketWorkflowPolicy.IsFinal(from);
        }

        return TicketWorkflowPolicy.CanTransition(from, to, isAdmin: false);
    }

    public static string FormatMessage(string? template, string defaultTemplate, int ticketId, string title) =>
        (string.IsNullOrWhiteSpace(template) ? defaultTemplate : template)
            .Replace("{id}", ticketId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{titulo}", title, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Frase legível de uma regra, para a lista: "Quando ..., se ..., então ...".</summary>
public static class AutomationDescriber
{
    public static string Describe(AutomationRule rule, string? categoryName, string? agentName)
    {
        var when = rule.Trigger switch
        {
            AutomationTrigger.TicketCreated => "um chamado for criado",
            AutomationTrigger.StatusChanged => rule.ConditionStatus is { } s ? $"o status mudar para \"{s.Label()}\"" : "o status mudar",
            AutomationTrigger.SlaWarning => "o SLA de um chamado entrar em risco",
            AutomationTrigger.SlaBreached => "o SLA de um chamado vencer",
            AutomationTrigger.Inactivity => $"um chamado ficar parado por {FormatHours(rule.ConditionInactiveHours)}",
            _ => "algo acontecer"
        };

        var conditions = new List<string>();
        if (rule.ConditionCategoryId.HasValue)
        {
            conditions.Add($"a categoria for \"{categoryName ?? "?"}\"");
        }

        if (rule.ConditionPriority is { } p)
        {
            conditions.Add($"a prioridade for {p.Label()}");
        }

        // No gatilho "Status alterado" o status já aparece em "quando".
        if (rule.ConditionStatus is { } cs && rule.Trigger != AutomationTrigger.StatusChanged)
        {
            conditions.Add($"o status for \"{cs.Label()}\"");
        }

        var words = RuleMatcher.SplitKeywords(rule.ConditionKeywords);
        if (words.Count > 0)
        {
            conditions.Add($"o texto citar {string.Join(" ou ", words.Select(w => $"\"{w}\""))}");
        }

        var action = rule.ActionType switch
        {
            AutomationActionType.AssignToAgent => $"atribuir a {agentName ?? "um atendente"}",
            AutomationActionType.SetPriority => $"mudar a prioridade para {(rule.ActionPriority?.Label() ?? "?")}",
            AutomationActionType.ChangeStatus => $"mudar o status para \"{(rule.ActionStatus?.Label() ?? "?")}\"",
            AutomationActionType.NotifyAdmins => "avisar os administradores",
            AutomationActionType.NotifyTeam => "avisar o responsável (ou a fila)",
            AutomationActionType.NotifyRequester => "avisar o solicitante",
            _ => "fazer algo"
        };

        var sentence = $"Quando {when}";
        if (conditions.Count > 0)
        {
            sentence += $", se {string.Join(" e ", conditions)}";
        }

        return $"{sentence}, então {action}.";
    }

    private static string FormatHours(int? hours) => hours switch
    {
        null => "?",
        < 48 => $"{hours} h",
        _ when hours % 24 == 0 => $"{hours / 24} dias",
        _ => $"{hours} h"
    };
}
