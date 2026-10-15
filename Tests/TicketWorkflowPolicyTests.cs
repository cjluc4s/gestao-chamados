using gestao_chamados.Data;
using gestao_chamados.Models;

namespace gestao_chamados.Tests;

public class TicketWorkflowPolicyTests
{
    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.WaitingUser)]
    [InlineData(TicketStatus.WaitingUser, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress)] // reabertura
    public void CanTransition_AllowsWorkflowSteps(TicketStatus from, TicketStatus to)
    {
        Assert.True(TicketWorkflowPolicy.CanTransition(from, to, isAdmin: false));
    }

    [Theory]
    [InlineData(TicketStatus.InProgress, TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Open)]
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Closed, TicketStatus.Open)]
    public void CanTransition_BlocksInvalidStepsForAgents(TicketStatus from, TicketStatus to)
    {
        Assert.False(TicketWorkflowPolicy.CanTransition(from, to, isAdmin: false));
    }

    [Fact]
    public void Closed_IsFinalForAgents()
    {
        Assert.Empty(TicketWorkflowPolicy.GetAllowedTransitions(TicketStatus.Closed, isAdmin: false));
    }

    [Fact]
    public void Closed_CanBeReopenedByAdmin()
    {
        Assert.True(TicketWorkflowPolicy.CanTransition(TicketStatus.Closed, TicketStatus.InProgress, isAdmin: true));
        Assert.False(TicketWorkflowPolicy.CanTransition(TicketStatus.Closed, TicketStatus.Open, isAdmin: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void CanTransition_RejectsUndefinedStatus(int rawStatus)
    {
        // Valor fora do enum vindo do POST (ex: status=99) não pode ser gravado.
        Assert.False(TicketWorkflowPolicy.CanTransition(TicketStatus.Open, (TicketStatus)rawStatus, isAdmin: true));
    }

    [Fact]
    public void AllowedTransitions_NeverIncludeCurrentStatus()
    {
        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            Assert.DoesNotContain(status, TicketWorkflowPolicy.GetAllowedTransitions(status, isAdmin: true));
        }
    }
}
