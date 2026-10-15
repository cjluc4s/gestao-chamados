using gestao_chamados.Models;

namespace gestao_chamados.Data;

public enum AssignmentCheck
{
    Ok,
    Unchanged,
    TicketFinal,
    NotAnAgent
}

/// <summary>
/// Atribuição feita pelo Admin. Atendentes continuam usando "Assumir chamado",
/// que só pega chamados sem dono (ver TicketAccessPolicy).
/// </summary>
public static class TicketAssignmentPolicy
{
    /// <param name="newAssigneeId">Atendente escolhido, ou null para devolver o chamado à fila.</param>
    /// <param name="agentIds">Ids dos usuários com papel de Atendente.</param>
    public static AssignmentCheck Check(TicketStatus status, string? currentAssigneeId, string? newAssigneeId, IReadOnlyCollection<string> agentIds)
    {
        if (TicketWorkflowPolicy.IsFinal(status))
        {
            return AssignmentCheck.TicketFinal;
        }

        if (newAssigneeId == currentAssigneeId)
        {
            return AssignmentCheck.Unchanged;
        }

        // Só Atendente pode receber chamado: um Usuário atribuído não enxergaria
        // o chamado (TicketAccessPolicy) e ele ficaria preso fora da fila.
        if (newAssigneeId is not null && !agentIds.Contains(newAssigneeId))
        {
            return AssignmentCheck.NotAnAgent;
        }

        return AssignmentCheck.Ok;
    }
}
