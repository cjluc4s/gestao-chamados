namespace gestao_chamados.Models;

public static class AutomationLabels
{
    public static string Label(this AutomationTrigger trigger) => trigger switch
    {
        AutomationTrigger.TicketCreated => "Chamado criado",
        AutomationTrigger.StatusChanged => "Status alterado",
        AutomationTrigger.SlaWarning => "SLA em risco",
        AutomationTrigger.SlaBreached => "SLA vencido",
        AutomationTrigger.Inactivity => "Chamado parado (inatividade)",
        _ => trigger.ToString()
    };

    public static string Label(this AutomationActionType action) => action switch
    {
        AutomationActionType.AssignToAgent => "Atribuir a um atendente",
        AutomationActionType.SetPriority => "Mudar a prioridade",
        AutomationActionType.ChangeStatus => "Mudar o status",
        AutomationActionType.NotifyAdmins => "Avisar os administradores",
        AutomationActionType.NotifyTeam => "Avisar o responsável (ou a fila)",
        AutomationActionType.NotifyRequester => "Avisar o solicitante",
        _ => action.ToString()
    };
}
