using System.Security.Claims;
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

    [Theory]
    [InlineData(new[] { RoleNames.Admin, RoleNames.Agent }, RoleNames.Admin)]
    [InlineData(new[] { RoleNames.Agent, RoleNames.User }, RoleNames.User)]
    [InlineData(new[] { RoleNames.Agent }, RoleNames.Agent)]
    [InlineData(new string[0], "")]
    public void ResolveRole_FollowsPrecedence(string[] roles, string expected)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        Assert.Equal(expected, TicketAccessPolicy.ResolveRole(principal));
    }

    [Theory]
    [InlineData(RoleNames.Admin, "admin-1")]
    [InlineData(RoleNames.User, CreatorId)]
    [InlineData(RoleNames.User, "other-user")]
    [InlineData(RoleNames.Agent, AgentId)]
    [InlineData(RoleNames.Agent, OtherAgentId)]
    [InlineData("", "some-user")]
    public void ApplyVisibility_MatchesCanAccess(string role, string currentUserId)
    {
        // O filtro de listagem (telas, dashboard, API) e a checagem individual
        // (detalhes, download, ações) precisam concordar sempre.
        var tickets = new List<Ticket>
        {
            new() { Id = 1, CreatedByUserId = CreatorId, AssignedToUserId = null },
            new() { Id = 2, CreatedByUserId = CreatorId, AssignedToUserId = AgentId },
            new() { Id = 3, CreatedByUserId = "other-user", AssignedToUserId = OtherAgentId },
            new() { Id = 4, CreatedByUserId = "other-user", AssignedToUserId = null }
        };

        var visible = TicketAccessPolicy.ApplyVisibility(tickets.AsQueryable(), role, currentUserId)
            .Select(t => t.Id)
            .ToHashSet();

        foreach (var ticket in tickets)
        {
            var canAccess = TicketAccessPolicy.CanAccess(role, currentUserId, ticket.CreatedByUserId, ticket.AssignedToUserId);
            Assert.Equal(canAccess, visible.Contains(ticket.Id));
        }
    }
}
