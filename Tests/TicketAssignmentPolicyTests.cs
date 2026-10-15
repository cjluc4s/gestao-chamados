using gestao_chamados.Data;
using gestao_chamados.Models;

namespace gestao_chamados.Tests;

public class TicketAssignmentPolicyTests
{
    private static readonly string[] Agents = ["agent-1", "agent-2"];

    [Fact]
    public void Check_AllowsAssigningUnassignedTicketToAgent()
    {
        Assert.Equal(AssignmentCheck.Ok, TicketAssignmentPolicy.Check(TicketStatus.Open, null, "agent-1", Agents));
    }

    [Fact]
    public void Check_AllowsReassigningToAnotherAgent()
    {
        Assert.Equal(AssignmentCheck.Ok, TicketAssignmentPolicy.Check(TicketStatus.InProgress, "agent-1", "agent-2", Agents));
    }

    [Fact]
    public void Check_AllowsReturningTicketToQueue()
    {
        Assert.Equal(AssignmentCheck.Ok, TicketAssignmentPolicy.Check(TicketStatus.WaitingUser, "agent-1", null, Agents));
    }

    [Theory]
    [InlineData("agent-1")]
    [InlineData(null)]
    public void Check_ReportsUnchangedAssignment(string? assignee)
    {
        Assert.Equal(AssignmentCheck.Unchanged, TicketAssignmentPolicy.Check(TicketStatus.Open, assignee, assignee, Agents));
    }

    [Theory]
    [InlineData("user-1")]   // usuário comum ou admin: não enxergaria o chamado como atendente
    [InlineData("inexistente")]
    public void Check_RejectsNonAgents(string assignee)
    {
        Assert.Equal(AssignmentCheck.NotAnAgent, TicketAssignmentPolicy.Check(TicketStatus.Open, null, assignee, Agents));
    }

    [Theory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public void Check_BlocksFinalTickets(TicketStatus status)
    {
        Assert.Equal(AssignmentCheck.TicketFinal, TicketAssignmentPolicy.Check(status, null, "agent-1", Agents));
    }
}
