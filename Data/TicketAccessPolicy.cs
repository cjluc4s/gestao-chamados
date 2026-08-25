using gestao_chamados.Models;

namespace gestao_chamados.Data;

public static class TicketAccessPolicy
{
    public static bool CanAccess(string role, string? currentUserId, string ticketCreatedByUserId, string? ticketAssignedToUserId)
    {
        if (role == RoleNames.Admin)
        {
            return true;
        }

        if (currentUserId is null)
        {
            return false;
        }

        return role switch
        {
            RoleNames.User => ticketCreatedByUserId == currentUserId,
            RoleNames.Agent => ticketAssignedToUserId == currentUserId || ticketAssignedToUserId is null,
            _ => false // papel não reconhecido: nega acesso por padrão
        };
    }
}
