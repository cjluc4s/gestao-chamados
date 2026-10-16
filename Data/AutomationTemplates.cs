using gestao_chamados.Models;
using gestao_chamados.Models.ViewModels;

namespace gestao_chamados.Data;

/// <summary>Regras prontas para começar rápido: preenchem o formulário, e o Admin ajusta e salva.</summary>
public static class AutomationTemplates
{
    public static readonly IReadOnlyList<AutomationTemplate> All =
    [
        new("fechar-resolvidos", "Fechar chamados resolvidos sem retorno",
            "Resolvido há 3 dias sem nenhuma movimentação → muda para Fechado."),
        new("lembrar-solicitante", "Lembrar o solicitante que aguardamos resposta",
            "Aguardando usuário há 2 dias → avisa o solicitante."),
        new("cancelar-sem-resposta", "Cancelar chamados sem resposta do solicitante",
            "Aguardando usuário há 7 dias → muda para Cancelado."),
        new("sla-critico", "Avisar os admins quando um chamado crítico vencer",
            "SLA vencido em prioridade Crítica → avisa os administradores."),
        new("palavras-urgentes", "Subir a prioridade por palavra-chave",
            "Chamado criado citando \"fora do ar\", \"parado\" ou \"urgente\" → prioridade Alta.")
    ];

    public static AutomationRuleForm? Create(string? key) => key switch
    {
        "fechar-resolvidos" => new()
        {
            Name = "Fechar chamados resolvidos sem retorno",
            Trigger = AutomationTrigger.Inactivity,
            ConditionStatus = TicketStatus.Resolved,
            ConditionInactiveHours = 72,
            ActionType = AutomationActionType.ChangeStatus,
            ActionStatus = TicketStatus.Closed
        },
        "lembrar-solicitante" => new()
        {
            Name = "Lembrar o solicitante que aguardamos resposta",
            Trigger = AutomationTrigger.Inactivity,
            ConditionStatus = TicketStatus.WaitingUser,
            ConditionInactiveHours = 48,
            ActionType = AutomationActionType.NotifyRequester,
            ActionMessage = "Seu chamado #{id} ({titulo}) está aguardando a sua resposta."
        },
        "cancelar-sem-resposta" => new()
        {
            Name = "Cancelar chamados sem resposta do solicitante",
            Trigger = AutomationTrigger.Inactivity,
            ConditionStatus = TicketStatus.WaitingUser,
            ConditionInactiveHours = 168,
            ActionType = AutomationActionType.ChangeStatus,
            ActionStatus = TicketStatus.Cancelled
        },
        "sla-critico" => new()
        {
            Name = "Avisar os admins quando um chamado crítico vencer",
            Trigger = AutomationTrigger.SlaBreached,
            ConditionPriority = TicketPriority.Critical,
            ActionType = AutomationActionType.NotifyAdmins,
            ActionMessage = "URGENTE: o chamado crítico #{id} estourou o SLA — {titulo}"
        },
        "palavras-urgentes" => new()
        {
            Name = "Subir a prioridade por palavra-chave",
            Trigger = AutomationTrigger.TicketCreated,
            ConditionKeywords = "fora do ar, parado, urgente",
            ActionType = AutomationActionType.SetPriority,
            ActionPriority = TicketPriority.High
        },
        _ => null
    };
}
