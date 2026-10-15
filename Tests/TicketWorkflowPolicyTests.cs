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
    [InlineData(TicketStatus.Cancelled, TicketStatus.InProgress)]
    public void CanTransition_BlocksInvalidStepsForAgents(TicketStatus from, TicketStatus to)
    {
        Assert.False(TicketWorkflowPolicy.CanTransition(from, to, isAdmin: false));
    }

    [Theory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public void FinalStatuses_CanBeReopenedOnlyByAdmin(TicketStatus final)
    {
        Assert.True(TicketWorkflowPolicy.IsFinal(final));
        Assert.Empty(TicketWorkflowPolicy.GetAllowedTransitions(final, isAdmin: false));
        Assert.True(TicketWorkflowPolicy.CanTransition(final, TicketStatus.InProgress, isAdmin: true));
        Assert.False(TicketWorkflowPolicy.CanTransition(final, TicketStatus.Open, isAdmin: true));
    }

    [Fact]
    public void Cancelled_IsNeverOfferedAsStatusChange()
    {
        // Cancelar tem ação própria (CanCancel); não aparece no "Alterar status".
        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            Assert.False(TicketWorkflowPolicy.CanTransition(status, TicketStatus.Cancelled, isAdmin: true));
        }
    }

    [Fact]
    public void CanEdit_CreatorOnlyWhileOpen()
    {
        Assert.True(TicketWorkflowPolicy.CanEdit(TicketStatus.Open, isAdmin: false, isCreator: true));

        foreach (var status in Enum.GetValues<TicketStatus>().Where(s => s != TicketStatus.Open))
        {
            Assert.False(TicketWorkflowPolicy.CanEdit(status, isAdmin: false, isCreator: true));
        }
    }

    [Fact]
    public void CanEdit_AdminAnyTime_OthersNever()
    {
        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            Assert.True(TicketWorkflowPolicy.CanEdit(status, isAdmin: true, isCreator: false));
            Assert.False(TicketWorkflowPolicy.CanEdit(status, isAdmin: false, isCreator: false));
        }
    }

    [Fact]
    public void CanCancel_CreatorOnlyWhileOpen()
    {
        Assert.True(TicketWorkflowPolicy.CanCancel(TicketStatus.Open, isAdmin: false, isCreator: true));
        Assert.False(TicketWorkflowPolicy.CanCancel(TicketStatus.InProgress, isAdmin: false, isCreator: true));
        Assert.False(TicketWorkflowPolicy.CanCancel(TicketStatus.Open, isAdmin: false, isCreator: false));
    }

    [Theory]
    [InlineData(TicketStatus.Open, true)]
    [InlineData(TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.WaitingUser, true)]
    [InlineData(TicketStatus.Resolved, true)]
    [InlineData(TicketStatus.Closed, false)]
    [InlineData(TicketStatus.Cancelled, false)]
    public void CanCancel_AdminWhileNotFinal(TicketStatus status, bool expected)
    {
        Assert.Equal(expected, TicketWorkflowPolicy.CanCancel(status, isAdmin: true, isCreator: false));
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
