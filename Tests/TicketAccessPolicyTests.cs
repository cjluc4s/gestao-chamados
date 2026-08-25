using gestao_chamados.Data;
using gestao_chamados.Models;

namespace gestao_chamados.Tests;

public class TicketAccessPolicyTests
{
    private const string CreatorId = "user-1";
    private const string AgentId = "agent-1";
    private const string OtherAgentId = "agent-2";

    [Fact]
    public void Admin_CanAccessAnyTicket()
    {
        Assert.True(TicketAccessPolicy.CanAccess(RoleNames.Admin, "admin-1", CreatorId, AgentId));
    }

    [Fact]
    public void User_CanAccessOwnTicket()
    {
        Assert.True(TicketAccessPolicy.CanAccess(RoleNames.User, CreatorId, CreatorId, null));
    }

    [Fact]
    public void User_CannotAccessOthersTicket()
    {
        Assert.False(TicketAccessPolicy.CanAccess(RoleNames.User, "other-user", CreatorId, null));
    }

    [Fact]
    public void Agent_CanAccessUnassignedTicket()
    {
        Assert.True(TicketAccessPolicy.CanAccess(RoleNames.Agent, AgentId, CreatorId, ticketAssignedToUserId: null));
    }

    [Fact]
    public void Agent_CanAccessTicketAssignedToSelf()
    {
        Assert.True(TicketAccessPolicy.CanAccess(RoleNames.Agent, AgentId, CreatorId, AgentId));
    }

    [Fact]
    public void Agent_CannotAccessTicketAssignedToAnotherAgent()
    {
        // Regressão do fix "roubo de chamado": um agente nao pode assumir
        // nem visualizar um chamado que ja esta com outro agente.
        Assert.False(TicketAccessPolicy.CanAccess(RoleNames.Agent, OtherAgentId, CreatorId, AgentId));
    }

    [Fact]
    public void UnrecognizedRole_IsDeniedByDefault()
    {
        // Regressão do fix "fail-open": usuario autenticado sem papel
        // reconhecido (ex: autocadastro antes do fix) nao deve enxergar nada.
        Assert.False(TicketAccessPolicy.CanAccess(role: "", "some-user", CreatorId, null));
    }

    [Fact]
    public void NullCurrentUserId_IsDeniedForNonAdminRoles()
    {
        Assert.False(TicketAccessPolicy.CanAccess(RoleNames.User, null, CreatorId, null));
        Assert.False(TicketAccessPolicy.CanAccess(RoleNames.Agent, null, CreatorId, null));
    }
}
