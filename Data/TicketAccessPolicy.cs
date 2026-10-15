using System.Security.Claims;
using gestao_chamados.Models;

namespace gestao_chamados.Data;

public static class TicketAccessPolicy
{
    /// <summary>
    /// Papel efetivo do usuário para fins de acesso a chamados. A precedência
    /// (Admin &gt; User &gt; Agent) é a mesma em todas as telas e na API.
    /// </summary>
    public static string ResolveRole(ClaimsPrincipal user) =>
        user.IsInRole(RoleNames.Admin) ? RoleNames.Admin
        : user.IsInRole(RoleNames.User) ? RoleNames.User
        : user.IsInRole(RoleNames.Agent) ? RoleNames.Agent
        : string.Empty;

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

    /// <summary>
    /// Mesma regra de <see cref="CanAccess"/>, aplicada como filtro de consulta.
    /// </summary>
    public static IQueryable<Ticket> ApplyVisibility(IQueryable<Ticket> query, string role, string currentUserId) => role switch
    {
        RoleNames.Admin => query,
        RoleNames.User => query.Where(t => t.CreatedByUserId == currentUserId),
        RoleNames.Agent => query.Where(t => t.AssignedToUserId == currentUserId || t.AssignedToUserId == null),
        _ => query.Where(t => false) // papel não reconhecido: nega acesso por padrão
    };
}
