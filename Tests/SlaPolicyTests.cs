using gestao_chamados.Data;
using gestao_chamados.Models;

namespace gestao_chamados.Tests;

public class SlaPolicyTests
{
    [Theory]
    [InlineData(TicketPriority.Critical, 2)]
    [InlineData(TicketPriority.High, 4)]
    [InlineData(TicketPriority.Medium, 12)]
    [InlineData(TicketPriority.Low, 24)]
    public void GetDeadline_ReturnsExpectedWindow(TicketPriority priority, double expectedHours)
    {
        var deadline = SlaPolicy.GetDeadline(priority);

        Assert.Equal(TimeSpan.FromHours(expectedHours), deadline);
    }

    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.WaitingUser)]
    public void IsBreached_TrueWhenOverdueAndStillOpen(TicketStatus status)
    {
        Assert.True(SlaPolicy.IsBreached(Now.AddMinutes(-1), status, Now));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public void IsBreached_FalseOnceResolvedOrClosed(TicketStatus status)
    {
        Assert.False(SlaPolicy.IsBreached(Now.AddHours(-5), status, Now));
    }

    [Fact]
    public void IsBreached_FalseBeforeDeadlineOrWithoutDeadline()
    {
        Assert.False(SlaPolicy.IsBreached(Now.AddMinutes(1), TicketStatus.Open, Now));
        Assert.False(SlaPolicy.IsBreached(null, TicketStatus.Open, Now));
    }

    [Fact]
    public void RecalculateDueAt_KeepsDeadlineWhenPriorityUnchanged()
    {
        var created = Now.AddHours(-1);
        var currentDue = Now.AddHours(30); // ex: limite antigo, de antes de uma mudança de regra

        Assert.Equal(currentDue, SlaPolicy.RecalculateDueAt(created, currentDue, TicketPriority.Low, TicketPriority.Low));
    }

    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Critical, 2)]
    [InlineData(TicketPriority.Critical, TicketPriority.Low, 24)]
    [InlineData(TicketPriority.Medium, TicketPriority.High, 4)]
    public void RecalculateDueAt_CountsFromCreationWhenPriorityChanges(TicketPriority from, TicketPriority to, double expectedHours)
    {
        var created = Now.AddHours(-1);
        var currentDue = SlaPolicy.GetDueAt(created, from);

        Assert.Equal(created.AddHours(expectedHours), SlaPolicy.RecalculateDueAt(created, currentDue, from, to));
    }

    [Fact]
    public void RecalculateDueAt_RaisingPriorityOfOldTicketMakesItOverdue()
    {
        var created = Now.AddHours(-5);
        var newDue = SlaPolicy.RecalculateDueAt(created, SlaPolicy.GetDueAt(created, TicketPriority.Low), TicketPriority.Low, TicketPriority.Critical);

        Assert.True(SlaPolicy.IsBreached(newDue, TicketStatus.Open, Now));
    }
}
